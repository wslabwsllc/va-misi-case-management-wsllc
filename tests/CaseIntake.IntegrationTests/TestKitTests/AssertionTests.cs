namespace CaseIntake.IntegrationTests.TestKitTests;

/// <summary>
/// The shared assertion helper (CaseIntake.Testing.Assert) must fail exactly when the check is false, report NUnit
/// failures, and let a retrying caller (bUnit's WaitForAssertion) recover without the earlier attempt failing the test.
/// </summary>
public class AssertionTests
{
    private static void Fails(Action check) =>
        NUnit.Framework.Assert.Throws<AssertionException>(() => check());

    [Test]
    public void Equality_and_collections_fail_when_they_differ()
    {
        Fails(() => Assert.Equal(1, 2));
        Fails(() => Assert.Equal("a", "b"));
        Fails(() => Assert.Equal(["a", "b"], new[] { "b", "a" }));   // order matters
        Fails(() => Assert.NotEqual(3, 3));
        Fails(() => Assert.Single(new[] { 1, 2 }));
        Fails(() => Assert.Empty(new[] { 1 }));
        Fails(() => Assert.Contains(3, new[] { 1, 2 }));
        Fails(() => Assert.Contains("x", "abc"));
        Fails(() => Assert.DoesNotContain("b", "abc"));
        Fails(() => Assert.True(false));
        Fails(() => Assert.Null("value"));
        Fails(() => Assert.NotNull(null));
        Fails(() => Assert.StartsWith("b", "abc", StringComparison.Ordinal));
        Fails(() => Assert.Matches("^z", "abc"));
        Fails(() => Assert.All(new[] { 1, 2, 3 }, i => Assert.True(i < 3)));
        Fails(() => Assert.Throws<InvalidOperationException>(() => { }));
        Fails(() => Assert.Throws<ArgumentException>(() => throw new ArgumentNullException("x")));   // exact type
        Fails(() => Assert.IsType<string>(1));
    }

    [Test]
    public void Passing_checks_pass_and_return_what_they_found()
    {
        Assert.Equal(["a", "b"], new List<string> { "a", "b" });
        Assert.Equal(2, Assert.Single(new[] { 2 }));
        Assert.Equal(3, Assert.Single(new[] { 1, 3 }, i => i > 2));
        Assert.Equal("x", Assert.Throws<ArgumentNullException>(() => throw new ArgumentNullException("x")).ParamName);
        Assert.NotNull(Assert.ThrowsAny<ArgumentException>(() => throw new ArgumentNullException("y")));
        Assert.Equal(5, Assert.IsType<int>(5));
        Assert.Equal("v", Assert.Contains("k", new Dictionary<string, string> { ["k"] = "v" }));
        Assert.InRange(5, 1, 9);
    }

    [Test]
    public void A_failed_attempt_that_is_retried_does_not_fail_the_test()
    {
        // bUnit's WaitForAssertion catches the failure and tries again; only the final attempt may decide the result.
        var attempts = 0;
        while (true)
        {
            attempts++;
            try
            {
                Assert.Equal(3, attempts);
                break;
            }
            catch (AssertionException) when (attempts < 3)
            {
            }
        }
        Assert.Equal(3, attempts);
    }
}
