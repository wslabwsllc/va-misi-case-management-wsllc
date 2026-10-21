using NUnit.Framework;

namespace CaseIntake.Testing;

/// <summary>
/// Asynchronous set-up and tear-down of a shared fixture (a database, a web host, a browser). A test class's own
/// per-test set-up uses NUnit's <see cref="SetUpAttribute"/> and <see cref="TearDownAttribute"/> instead.
/// </summary>
public interface IAsyncLifetime
{
    Task InitializeAsync();

    Task DisposeAsync();
}

/// <summary>
/// One <typeparamref name="TFixture"/> per test class (<typeparamref name="TSelf"/>), created before the class's first
/// test and disposed after its last: the scope of an xUnit class fixture. With
/// <see cref="LifeCycle.InstancePerTestCase"/> (set for every test assembly), each test still gets a fresh instance of
/// the test class, so only the fixture is shared.
/// </summary>
public abstract class ClassFixture<TSelf, TFixture>
    where TSelf : ClassFixture<TSelf, TFixture>
    where TFixture : class
{
    private static TFixture? s_shared;

    /// <summary>The fixture shared by the tests of <typeparamref name="TSelf"/>.</summary>
    protected static TFixture Shared => s_shared ?? throw new InvalidOperationException($"{typeof(TFixture).Name} is not initialized.");

    [OneTimeSetUp]
    public static async Task CreateSharedFixtureAsync()
    {
        var fixture = Activator.CreateInstance<TFixture>();
        if (fixture is IAsyncLifetime lifetime)
        {
            await lifetime.InitializeAsync();
        }
        s_shared = fixture;
    }

    [OneTimeTearDown]
    public static async Task DisposeSharedFixtureAsync()
    {
        var fixture = s_shared;
        s_shared = null;
        switch (fixture)
        {
            case IAsyncLifetime lifetime:
                await lifetime.DisposeAsync();
                if (fixture is IAsyncDisposable alsoAsync)
                {
                    await alsoAsync.DisposeAsync();
                }
                else if (fixture is IDisposable alsoSync)
                {
                    alsoSync.Dispose();
                }
                break;
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync();
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }
}
