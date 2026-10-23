namespace CaseIntake.VeteranLookup.Client;

/// <summary>
/// Settings both lookup services share. The timeouts default to the adopted budget (E4-S3): at most one retry, a
/// two-second limit per attempt and five seconds in total; tests shrink them.
/// </summary>
public abstract class LookupServiceOptions
{
    /// <summary>Base URL of the service, e.g. <c>http://veteran-lookup:8081</c>.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);
}

/// <summary>Bound from the <c>VeteranLookup</c> configuration section.</summary>
public sealed class VeteranLookupClientOptions : LookupServiceOptions
{
    public const string SectionName = "VeteranLookup";
}

/// <summary>Bound from the <c>VeteranHistory</c> configuration section (Change 2), with the same defaults as the Lookup.</summary>
public sealed class VeteranHistoryClientOptions : LookupServiceOptions
{
    public const string SectionName = "VeteranHistory";
}
