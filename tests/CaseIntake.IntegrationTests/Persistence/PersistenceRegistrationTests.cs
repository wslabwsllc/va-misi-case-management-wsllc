using CaseIntake.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CaseIntake.IntegrationTests.Persistence;

public class PersistenceRegistrationTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void A_missing_connection_string_fails_at_startup_instead_of_falling_back(string? connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:CaseIntake"] = connectionString })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddPersistence(configuration));

        Assert.Contains("ConnectionStrings__CaseIntake", ex.Message);
    }
}
