using System.Text.Json;
using System.Text.Json.Serialization;

namespace CaseIntake.VeteranLookup.Mock;

/// <summary>Request body of <c>POST /veterans/lookup</c>. <c>ssn</c> is required; a missing or non-string value is a 400.</summary>
public sealed record VeteranLookupRequest
{
    [JsonRequired]
    public required string Ssn { get; init; }
}

/// <summary>Successful lookup: the veteran's name and mailing address (synthetic data only).</summary>
public sealed record VeteranLookupResponse
{
    [JsonRequired]
    public required string Name { get; init; }

    [JsonRequired]
    public required string Address { get; init; }
}

/// <summary>One fixture row of the embedded dataset.</summary>
public sealed record VeteranRecord(string Ssn, string Name, string Address);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(VeteranLookupRequest))]
[JsonSerializable(typeof(VeteranLookupResponse))]
[JsonSerializable(typeof(VeteranRecord[]))]
internal sealed partial class MockJsonContext : JsonSerializerContext;
