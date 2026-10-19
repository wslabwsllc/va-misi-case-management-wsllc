using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CaseIntake.Web.Services;

/// <summary>
/// The one application JS module (<c>wwwroot/js/interop.js</c>), imported lazily per circuit and disposed with it.
/// Calls are made only from <c>OnAfterRenderAsync</c> or event handlers, never during prerendering; a disconnected
/// circuit (<see cref="JSDisconnectedException"/>) is not an error, so those calls are ignored.
/// </summary>
public sealed class UiInterop(IJSRuntime js) : IAsyncDisposable, IDisposable
{
    public const string ModulePath = "./js/interop.js";

    private Task<IJSObjectReference>? _module;

    private Task<IJSObjectReference> Module => _module ??= js.InvokeAsync<IJSObjectReference>("import", ModulePath).AsTask();

    /// <summary>Focuses the first element matching <paramref name="selector"/> (used after navigation).</summary>
    public Task FocusSelectorAsync(string selector) => InvokeAsync("focusSelector", selector);

    /// <summary>Opens a native <c>&lt;dialog&gt;</c> modally (built-in focus trap and Esc) and reports close/cancel.</summary>
    public Task ShowDialogAsync<T>(ElementReference dialog, DotNetObjectReference<T> callbacks) where T : class =>
        InvokeAsync("showDialog", dialog, callbacks);

    public Task CloseDialogAsync(ElementReference dialog) => InvokeAsync("closeDialog", dialog);

    /// <summary>Sets a CSS transform scale on an image inside the evidence viewer.</summary>
    public Task SetZoomAsync(ElementReference image, double scale) => InvokeAsync("setZoom", image, scale);

    private async Task InvokeAsync(string identifier, params object?[] args)
    {
        try
        {
            var module = await Module;
            await module.InvokeVoidAsync(identifier, args);
        }
        catch (JSDisconnectedException)
        {
            // The circuit is gone; there is no browser to update.
        }
        catch (TaskCanceledException)
        {
            // The circuit is shutting down.
        }
    }

    /// <summary>Synchronous disposal (a container disposed synchronously): nothing to release without a JS round trip.</summary>
    public void Dispose() => _module = null;

    public async ValueTask DisposeAsync()
    {
        if (_module is null)
        {
            return;
        }
        try
        {
            var module = await _module;
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
        catch (TaskCanceledException)
        {
        }
        catch (JSException)
        {
            // The import itself failed (for example the page was unloading); nothing to release.
        }
    }
}
