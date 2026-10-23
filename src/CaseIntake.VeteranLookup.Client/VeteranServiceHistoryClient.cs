using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CaseIntake.Contracts.Security;
using CaseIntake.Contracts.VeteranLookup;
using CaseIntake.Domain.Cases;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CaseIntake.VeteranLookup.Client;

/// <summary>
/// Calls <c>POST /veterans/service-history/lookup</c> on the local Service History service (Change 2, E4-C2-3). Governed
/// by the existing <c>veteran.lookup</c> permission (D5): permission, then SSN format, are checked before any network
/// call (E3-C2-1). 200 with a body the case can store is <see cref="VeteranLookupOutcome.Found"/>, 404 is
/// <see cref="VeteranLookupOutcome.NotFound"/>; everything else, including an unusable body, is
/// <see cref="VeteranLookupOutcome.Unavailable"/>, so intake carries on without it. Transport, retries and budgets are
/// shared with the Lookup client (<see cref="LookupTransport"/>, <see cref="VeteranLookupModule"/>).
/// </summary>
internal sealed class VeteranServiceHistoryClient(
    HttpClient http,
    ICurrentUser currentUser,
    IOptions<VeteranHistoryClientOptions> options,
    TimeProvider timeProvider,
    ILogger<VeteranServiceHistoryClient> logger) : IVeteranServiceHistoryClient
{
    /// <summary>Relative to the base address, so a base URL with a path prefix keeps it.</summary>
    internal const string LookupPath = "veterans/service-history/lookup";

    private static readonly ServiceHistoryResult Forbidden = new(VeteranLookupOutcome.Forbidden, null);
    private static readonly ServiceHistoryResult InvalidInput = new(VeteranLookupOutcome.InvalidInput, null);
    private static readonly ServiceHistoryResult NotFound = new(VeteranLookupOutcome.NotFound, null);
    private static readonly ServiceHistoryResult Unavailable = new(VeteranLookupOutcome.Unavailable, null);

    public async Task<ServiceHistoryResult> LookupAsync(string ssn, CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        if (!actor.HasPermission(Permissions.VeteranLookup))
        {
            LookupLog.AuthorizationDenied(logger, actor.UserId, Permissions.VeteranLookup, null, nameof(VeteranServiceHistoryClient) + "." + nameof(LookupAsync));
            return Forbidden;
        }
        if (VeteranLookupClient.Normalize(ssn) is not { } normalized)
        {
            LookupLog.HistoryRejected(logger);
            return InvalidInput;
        }

        var started = timeProvider.GetTimestamp();
        return await LookupTransport.PostAsync(http, LookupPath, new HistoryRequest(normalized), HistoryJsonContext.Default.HistoryRequest,
            options.Value.TotalTimeout,
            (response, budget) => MapAsync(response, started, budget),
            exceptionType =>
            {
                LookupLog.HistoryUnavailable(logger, exceptionType, null, Elapsed(started));
                return Unavailable;
            },
            ct);
    }

    private async Task<ServiceHistoryResult> MapAsync(HttpResponseMessage response, long started, CancellationToken ct)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                LookupLog.HistoryCompleted(logger, nameof(VeteranLookupOutcome.NotFound), Elapsed(started));
                return NotFound;
            case HttpStatusCode.OK:
                HistoryResponse? body;
                try
                {
                    body = await response.Content.ReadFromJsonAsync(HistoryJsonContext.Default.HistoryResponse, ct);
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
                {
                    body = null;   // malformed JSON, a date that is not yyyy-MM-dd, or an undecodable charset
                }

                // The result becomes part of the case's snapshot, so it must satisfy the rules intake applies (E2-C2-1):
                // branch, start date and character of service present and within limits, and start not after end.
                if (body is { Branch: { } branch, ServiceStartDate: { } start, CharacterOfService: { } character })
                {
                    var history = new ServiceHistory(branch, start, body.ServiceEndDate, character, body.LastVerifiedDate);
                    if (CaseTextRules.ValidateServiceHistory(ServiceHistoryStatus.Found, history).Count == 0)
                    {
                        LookupLog.HistoryCompleted(logger, nameof(VeteranLookupOutcome.Found), Elapsed(started));
                        return new ServiceHistoryResult(VeteranLookupOutcome.Found, history);
                    }
                }

                LookupLog.HistoryUnavailable(logger, "UnusableResponse", (int)response.StatusCode, Elapsed(started));
                return Unavailable;
            default:
                LookupLog.HistoryUnavailable(logger, "UnexpectedStatus", (int)response.StatusCode, Elapsed(started));
                return Unavailable;
        }
    }

    private double Elapsed(long started) => timeProvider.GetElapsedTime(started).TotalMilliseconds;
}

internal sealed record HistoryRequest(string Ssn);

internal sealed record HistoryResponse(string? Branch, DateOnly? ServiceStartDate, DateOnly? ServiceEndDate,
    string? CharacterOfService, DateOnly? LastVerifiedDate);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(HistoryRequest))]
[JsonSerializable(typeof(HistoryResponse))]
internal sealed partial class HistoryJsonContext : JsonSerializerContext;
