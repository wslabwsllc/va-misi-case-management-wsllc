namespace CaseIntake.VeteranLookup.Client;

/// <summary>
/// Bound from the <c>VeteranLookup</c> configuration section. The timeouts default to the adopted budget (E4-S3):
/// at most one retry, a two-second limit per attempt and five seconds in total; tests shrink them.
/// </summary>
public sealed class VeteranLookupClientOptions
{
    public const string SectionName = "VeteranLookup";

    /// <summary>Base URL of the lookup service, e.g. <c>http://veteran-lookup:8081</c>.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);
}
