using CaseIntake.Contracts.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace CaseIntake.ServiceDefaults;

internal sealed class DatabaseHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringNames.CaseIntake);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return HealthCheckResult.Unhealthy($"Connection string '{ConnectionStringNames.CaseIntake}' is not configured.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never echo the exception message: connection strings and values can be embedded in it.
            return HealthCheckResult.Unhealthy("Database is unreachable.", exception: null,
                data: new Dictionary<string, object> { ["errorType"] = ex.GetType().Name });
        }
    }
}

internal sealed class VeteranLookupHealthCheck(IHttpClientFactory httpClientFactory, IConfiguration configuration) : IHealthCheck
{
    public const string HttpClientName = "veteran-lookup-health";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var baseUrl = configuration["VeteranLookup:BaseUrl"];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            return HealthCheckResult.Unhealthy("VeteranLookup:BaseUrl is not configured.");
        }

        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(HealthUri(baseUri), cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"Veteran lookup service returned {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Veteran lookup service is unreachable.", exception: null,
                data: new Dictionary<string, object> { ["errorType"] = ex.GetType().Name });
        }
    }

    /// <summary><c>{base}/health</c>, keeping any path prefix in the base URL (<c>http://host/lookup</c> → <c>http://host/lookup/health</c>).</summary>
    private static Uri HealthUri(Uri baseUri)
    {
        var withTrailingSlash = baseUri.AbsoluteUri.EndsWith('/') ? baseUri : new Uri(baseUri.AbsoluteUri + "/");
        return new Uri(withTrailingSlash, "health");
    }
}
