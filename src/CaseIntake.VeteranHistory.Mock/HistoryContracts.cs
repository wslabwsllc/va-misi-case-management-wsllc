using System.Text.Json;
using System.Text.Json.Serialization;

namespace CaseIntake.VeteranHistory.Mock;

/// <summary>Request body of <c>POST /veterans/service-history/lookup</c>. <c>ssn</c> is required; a missing or non-string value is a 400.</summary>
public sealed record ServiceHistoryRequest
{
    [JsonRequired]
    public required string Ssn { get; init; }
}

/// <summary>
/// Service history found: the four fields of the Government's example, plus <c>lastVerifiedDate</c> (D7), an optional
/// extension the requirement names but the example omits. An open <c>serviceEndDate</c> is someone still serving.
/// </summary>
public sealed record ServiceHistoryResponse
{
    [JsonRequired]
    public required string Branch { get; init; }

    [JsonRequired]
    public required DateOnly ServiceStartDate { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateOnly? ServiceEndDate { get; init; }

    [JsonRequired]
    public required string CharacterOfService { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateOnly? LastVerifiedDate { get; init; }
}

/// <summary>One fixture row of the embedded dataset.</summary>
public sealed record ServiceHistoryRecord(string Ssn, string Branch, DateOnly ServiceStartDate, DateOnly? ServiceEndDate,
    string CharacterOfService, DateOnly? LastVerifiedDate);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ServiceHistoryRequest))]
[JsonSerializable(typeof(ServiceHistoryResponse))]
[JsonSerializable(typeof(ServiceHistoryRecord[]))]
internal sealed partial class HistoryMockJsonContext : JsonSerializerContext;
