using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using NUnit.Framework;
using NUnit.Framework.Constraints;
using NUnit.Framework.Internal;

namespace CaseIntake.Testing;

/// <summary>
/// The assertions of the test suites. The call shapes are the ones the tests were written with (expected first;
/// <c>Single</c>, <c>IsType</c> and <c>Throws</c> return what they found), and every check is evaluated with NUnit's
/// constraint model (<see cref="Is"/>, <see cref="Does"/>, <see cref="Has"/>), so the messages are NUnit's.
/// <para>
/// A failure throws <see cref="AssertionException"/>, which NUnit reports as a failed test. Unlike
/// <c>NUnit.Framework.Assert.That</c>, a failure is not recorded on the test before it is thrown, so a caller that retries
/// an assertion until it passes (bUnit's <c>WaitForAssertion</c>, retry loops) is not marked failed by an earlier attempt.
/// </para>
/// </summary>
public static class Assert
{
    // ---------- Core ----------

    private static void That<TActual>(TActual actual, IResolveConstraint expression, string? message = null)
    {
        var result = expression.Resolve().ApplyTo(actual);
        if (!result.IsSuccess)
        {
            var writer = new TextMessageWriter(message);
            result.WriteMessageTo(writer);
            throw new AssertionException(writer.ToString());
        }
    }

    private static void Check([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
        {
            throw new AssertionException(message);
        }
    }

    [DoesNotReturn]
    public static void Fail(string message) => throw new AssertionException(message);

    // ---------- Equality ----------

    public static void Equal<T>(T expected, T actual) => That(actual, Is.EqualTo(expected));

    public static void Equal<T>(IEnumerable<T>? expected, IEnumerable<T>? actual) =>
        That(actual is null ? null : actual.ToList(), Is.EqualTo(expected is null ? null : expected.ToList()));

    public static void Equal(string? expected, string? actual) => That(actual, Is.EqualTo(expected));

    public static void Equal(string? expected, string? actual, bool ignoreCase)
    {
        if (ignoreCase)
        {
            That(actual, Is.EqualTo(expected).IgnoreCase);
        }
        else
        {
            That(actual, Is.EqualTo(expected));
        }
    }

    public static void Equal(double expected, double actual, int precision) =>
        That(Math.Round(actual, precision), Is.EqualTo(Math.Round(expected, precision)));

    public static void NotEqual<T>(T expected, T actual) => That(actual, Is.Not.EqualTo(expected));

    public static void NotEqual<T>(IEnumerable<T>? expected, IEnumerable<T>? actual) =>
        That(actual is null ? null : actual.ToList(), Is.Not.EqualTo(expected is null ? null : expected.ToList()));

    public static void Same(object? expected, object? actual) => That(actual, Is.SameAs(expected));

    public static void NotSame(object? expected, object? actual) => That(actual, Is.Not.SameAs(expected));

    // ---------- Booleans and null ----------

    public static void True([DoesNotReturnIf(false)] bool condition) => That(condition, Is.True);

    public static void True([DoesNotReturnIf(false)] bool condition, string? userMessage) => That(condition, Is.True, userMessage);

    public static void True([DoesNotReturnIf(false)] bool? condition) => That(condition, Is.True);

    public static void True([DoesNotReturnIf(false)] bool? condition, string? userMessage) => That(condition, Is.True, userMessage);

    public static void False([DoesNotReturnIf(true)] bool condition) => That(condition, Is.False);

    public static void False([DoesNotReturnIf(true)] bool condition, string? userMessage) => That(condition, Is.False, userMessage);

    public static void False([DoesNotReturnIf(true)] bool? condition) => That(condition, Is.False);

    public static void False([DoesNotReturnIf(true)] bool? condition, string? userMessage) => That(condition, Is.False, userMessage);

    public static void Null(object? value) => That(value, Is.Null);

    public static void NotNull([NotNull] object? value) => Check(value is not null, "Expected: not null, but was: null");

    public static T NotNull<T>(T? value) where T : struct
    {
        That(value, Is.Not.Null);
        return value!.Value;
    }

    // ---------- Collections ----------

    public static void Empty(IEnumerable collection) => That(collection, Is.Empty);

    public static void NotEmpty(IEnumerable collection) => That(collection, Is.Not.Empty);

    public static T Single<T>(IEnumerable<T> collection)
    {
        var items = collection.ToList();
        That(items, Has.Exactly(1).Items);
        return items[0];
    }

    public static T Single<T>(IEnumerable<T> collection, Predicate<T> predicate)
    {
        var matches = collection.Where(item => predicate(item)).ToList();
        Check(matches.Count == 1, $"Expected exactly one item to match the filter, but {matches.Count} did.");
        return matches[0];
    }

    public static object? Single(IEnumerable collection)
    {
        var items = collection.Cast<object?>().ToList();
        That(items, Has.Exactly(1).Items);
        return items[0];
    }

    public static void Contains<T>(T expected, IEnumerable<T> collection) => That(collection.ToList(), Has.Member(expected));

    public static void Contains<T>(IEnumerable<T> collection, Predicate<T> filter) =>
        Check(collection.Any(item => filter(item)), "Expected at least one item to match the filter, but none did.");

    public static TValue Contains<TKey, TValue>(TKey expected, IReadOnlyDictionary<TKey, TValue> collection)
    {
        That(collection.Keys.ToList(), Has.Member(expected));
        return collection[expected];
    }

    public static TValue Contains<TKey, TValue>(TKey expected, IDictionary<TKey, TValue> collection)
    {
        That(collection.Keys.ToList(), Has.Member(expected));
        return collection[expected];
    }

    public static TValue Contains<TKey, TValue>(TKey expected, Dictionary<TKey, TValue> collection) where TKey : notnull =>
        Contains(expected, (IDictionary<TKey, TValue>)collection);

    public static void DoesNotContain<T>(T expected, IEnumerable<T> collection) => That(collection.ToList(), Has.No.Member(expected));

    public static void DoesNotContain<T>(IEnumerable<T> collection, Predicate<T> filter)
    {
        var matches = collection.Where(item => filter(item)).ToList();
        Check(matches.Count == 0, $"Expected no item to match the filter, but {matches.Count} did: {string.Join(", ", matches.Take(5))}");
    }

    public static void DoesNotContain<TKey, TValue>(TKey expected, IReadOnlyDictionary<TKey, TValue> collection) =>
        That(collection.Keys.ToList(), Has.No.Member(expected));

    public static void DoesNotContain<TKey, TValue>(TKey expected, IDictionary<TKey, TValue> collection) =>
        That(collection.Keys.ToList(), Has.No.Member(expected));

    public static void DoesNotContain<TKey, TValue>(TKey expected, Dictionary<TKey, TValue> collection) where TKey : notnull =>
        DoesNotContain(expected, (IDictionary<TKey, TValue>)collection);

    /// <summary>Runs <paramref name="action"/> on every item and reports every failing item, not just the first.</summary>
    public static void All<T>(IEnumerable<T> collection, Action<T> action)
    {
        List<string> failures = [];
        var index = 0;
        foreach (var item in collection)
        {
            try
            {
                action(item);
            }
            catch (AssertionException ex)
            {
                failures.Add($"[{index}] {item}: {ex.Message}");
            }
            index++;
        }
        Check(failures.Count == 0, $"{failures.Count} out of {index} items did not pass:\n{string.Join("\n", failures)}");
    }

    public static void Subset<T>(ISet<T> expectedSuperset, ISet<T>? actual)
    {
        That(actual, Is.Not.Null);
        That(actual!.ToList(), Is.SubsetOf(expectedSuperset));
    }

    public static void ProperSubset<T>(ISet<T> expectedSuperset, ISet<T>? actual)
    {
        That(actual, Is.Not.Null);
        That(actual!.ToList(), Is.SubsetOf(expectedSuperset));
        Check(actual!.Count < expectedSuperset.Count, "Expected a proper subset, but the sets are equal.");
    }

    public static void InRange<T>(T actual, T low, T high) where T : IComparable<T> => That(actual, Is.InRange(low, high));

    // ---------- Strings ----------

    public static void Contains(string expectedSubstring, string? actualString) => That(actualString, Does.Contain(expectedSubstring));

    public static void Contains(string expectedSubstring, string? actualString, StringComparison comparisonType) =>
        Check(actualString is not null && actualString.Contains(expectedSubstring, comparisonType),
            $"Expected a string containing \"{expectedSubstring}\" ({comparisonType}), but was \"{actualString}\".");

    public static void DoesNotContain(string expectedSubstring, string? actualString) => That(actualString, Does.Not.Contain(expectedSubstring));

    public static void DoesNotContain(string expectedSubstring, string? actualString, StringComparison comparisonType) =>
        Check(actualString is null || !actualString.Contains(expectedSubstring, comparisonType),
            $"Expected a string not containing \"{expectedSubstring}\" ({comparisonType}), but was \"{actualString}\".");

    public static void StartsWith(string? expectedStartString, string? actualString) => That(actualString, Does.StartWith(expectedStartString ?? string.Empty));

    public static void StartsWith(string? expectedStartString, string? actualString, StringComparison comparisonType) =>
        Check(actualString is not null && actualString.StartsWith(expectedStartString ?? string.Empty, comparisonType),
            $"Expected a string starting with \"{expectedStartString}\" ({comparisonType}), but was \"{actualString}\".");

    public static void EndsWith(string? expectedEndString, string? actualString) => That(actualString, Does.EndWith(expectedEndString ?? string.Empty));

    public static void EndsWith(string? expectedEndString, string? actualString, StringComparison comparisonType) =>
        Check(actualString is not null && actualString.EndsWith(expectedEndString ?? string.Empty, comparisonType),
            $"Expected a string ending with \"{expectedEndString}\" ({comparisonType}), but was \"{actualString}\".");

    public static void Matches([StringSyntax(StringSyntaxAttribute.Regex)] string expectedRegexPattern, string? actualString) =>
        That(actualString, Does.Match(expectedRegexPattern));

    public static void Matches(Regex expectedRegex, string? actualString) =>
        Check(actualString is not null && expectedRegex.IsMatch(actualString), $"Expected a match for /{expectedRegex}/, but was \"{actualString}\".");

    public static void DoesNotMatch([StringSyntax(StringSyntaxAttribute.Regex)] string expectedRegexPattern, string? actualString) =>
        That(actualString, Does.Not.Match(expectedRegexPattern));

    public static void DoesNotMatch(Regex expectedRegex, string? actualString) =>
        Check(actualString is null || !expectedRegex.IsMatch(actualString), $"Expected no match for /{expectedRegex}/, but was \"{actualString}\".");

    // ---------- Types ----------

    public static T IsType<T>([NotNull] object? value)
    {
        Check(value is not null, $"Expected an instance of {typeof(T).Name}, but was null.");
        That(value, Is.TypeOf<T>());
        return (T)value!;
    }

    public static void IsType(Type expectedType, [NotNull] object? value)
    {
        Check(value is not null, $"Expected an instance of {expectedType.Name}, but was null.");
        That(value, Is.TypeOf(expectedType));
    }

    // ---------- Exceptions ----------

    /// <summary>The action throws exactly <typeparamref name="T"/> (not a derived type).</summary>
    public static T Throws<T>(Action testCode) where T : Exception => Exact<T>(Capture(testCode));

    public static T Throws<T>(Func<object?> testCode) where T : Exception => Exact<T>(Capture(() => testCode()));

    public static T Throws<T>(string? paramName, Action testCode) where T : ArgumentException
    {
        var exception = Exact<T>(Capture(testCode));
        Equal(paramName, exception.ParamName);
        return exception;
    }

    public static async Task<T> ThrowsAsync<T>(Func<Task> testCode) where T : Exception => Exact<T>(await CaptureAsync(testCode));

    /// <summary>The action throws <typeparamref name="T"/> or a type derived from it.</summary>
    public static T ThrowsAny<T>(Action testCode) where T : Exception => Derived<T>(Capture(testCode));

    public static T ThrowsAny<T>(Func<object?> testCode) where T : Exception => Derived<T>(Capture(() => testCode()));

    public static async Task<T> ThrowsAnyAsync<T>(Func<Task> testCode) where T : Exception => Derived<T>(await CaptureAsync(testCode));

    private static Exception? Capture(Action testCode)
    {
        try
        {
            testCode();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> testCode)
    {
        try
        {
            await testCode();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static T Exact<T>(Exception? exception) where T : Exception
    {
        Check(exception is not null, $"Expected {typeof(T).Name} to be thrown, but nothing was thrown.");
        Check(exception.GetType() == typeof(T), $"Expected {typeof(T).Name} to be thrown, but {exception.GetType().Name} was: {exception.Message}");
        return (T)exception;
    }

    private static T Derived<T>(Exception? exception) where T : Exception
    {
        Check(exception is not null, $"Expected {typeof(T).Name} (or a derived type) to be thrown, but nothing was thrown.");
        Check(exception is T, $"Expected {typeof(T).Name} (or a derived type) to be thrown, but {exception.GetType().Name} was: {exception.Message}");
        return (T)exception;
    }
}
