using System.Diagnostics.CodeAnalysis;
using CaseIntake.Web.Components.Kit;
using CaseIntake.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CaseIntake.Web.Tests;

/// <summary>
/// The JS interop wrappers tolerate a circuit that is gone (disconnected, cancelled, element removed): focus and dialog
/// calls are best-effort and never surface as errors.
/// </summary>
[Category("Story:E5-S1")]
public class UiInteropTests
{
    /// <summary>An IJSRuntime whose every call fails with <see cref="Failure"/> (or succeeds when it is null).</summary>
    private sealed class ThrowingRuntime(Exception? failure) : IJSRuntime
    {
        public Exception? Failure { get; set; } = failure;
        public List<string> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Calls.Add(identifier);
            return Failure is { } f ? ValueTask.FromException<TValue>(f) : ValueTask.FromResult(default(TValue)!);
        }
    }

    /// <summary>A module whose calls succeed and which records disposal.</summary>
    private sealed class RecordingModule : IJSObjectReference
    {
        public List<string> Calls { get; } = [];
        public bool Disposed { get; private set; }

        public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Calls.Add(identifier);
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ModuleRuntime(RecordingModule module) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            ValueTask.FromResult((TValue)(object)module);
    }

    public static TheoryData<Exception> CircuitGone => new() { new JSDisconnectedException("gone"), new TaskCanceledException() };
    [TestCaseSource(nameof(CircuitGone))]
    public async Task Calls_on_a_circuit_that_is_gone_are_ignored(Exception failure)
    {
        var runtime = new ThrowingRuntime(failure);
        var interop = new UiInterop(runtime);

        await interop.FocusSelectorAsync("h1");
        await interop.CloseDialogAsync(default);
        await interop.SetZoomAsync(default, 1.5);

        Assert.Equal("import", Assert.Single(runtime.Calls));
        await interop.DisposeAsync();
    }

    [Test]
    public async Task Module_calls_go_to_the_imported_module_and_disposal_releases_it()
    {
        var module = new RecordingModule();
        var interop = new UiInterop(new ModuleRuntime(module));

        await interop.FocusSelectorAsync("h1");
        await interop.SetZoomAsync(default, 2);
        await interop.DisposeAsync();

        Assert.Equal(["focusSelector", "setZoom"], module.Calls);
        Assert.True(module.Disposed);
    }
    [TestCaseSource(nameof(DisposeFailures))]
    public async Task Disposal_tolerates_a_failed_or_missing_import(Exception failure)
    {
        var interop = new UiInterop(new ThrowingRuntime(failure));
        await interop.DisposeAsync();   // never imported: nothing to release

        await interop.FocusSelectorAsync("h1").ContinueWith(_ => { }, TaskScheduler.Default);
        await interop.DisposeAsync();
        interop.Dispose();
        await interop.DisposeAsync();
    }

    public static TheoryData<Exception> DisposeFailures => new() { new JSDisconnectedException("gone"), new TaskCanceledException(), new JSException("import failed") };

    public static TheoryData<Exception> FocusFailures => new() { new JSException("detached"), new JSDisconnectedException("gone"), new InvalidOperationException("not rendered") };
    [TestCaseSource(nameof(FocusFailures))]
    public async Task Safe_focus_ignores_removed_elements_and_lost_circuits(Exception failure)
    {
        var runtime = new ThrowingRuntime(failure);
        var element = new ElementReference("el-1", new WebElementReferenceContext(runtime));

        await element.FocusSafeAsync();

        Assert.Single(runtime.Calls);
    }

    [Test]
    public async Task Safe_focus_skips_an_element_that_was_never_captured_and_focuses_one_that_was()
    {
        await default(ElementReference).FocusSafeAsync();

        var runtime = new ThrowingRuntime(null);
        await new ElementReference("el-2", new WebElementReferenceContext(runtime)).FocusSafeAsync();
        Assert.Single(runtime.Calls);
    }
}
