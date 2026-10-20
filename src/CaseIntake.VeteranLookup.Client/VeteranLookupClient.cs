using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CaseIntake.Contracts.Security;
using CaseIntake.Contracts.VeteranLookup;
using CaseIntake.Domain.Cases;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Timeout;

namespace CaseIntake.VeteranLookup.Client;

/// <summary>
/// Calls <c>POST /veterans/lookup</c> on the local lookup service (E4-S3, OQ-5c). Permission and SSN format are
/// checked before any network call. Every outcome is mapped to <see cref="VeteranLookupOutcome"/>: transport errors,
/// timeouts, unexpected statuses and unusable bodies are <see cref="VeteranLookupOutcome.Unavailable"/> so intake can
/// carry on without the lookup. Only the caller's own cancellation propagates as an exception. Retries and timeouts
/// come from the resilience pipeline registered in <see cref="VeteranLookupModule"/>; the total budget is applied
/// here so that it also covers reading the response body.
/// </summary>
internal sealed partial class VeteranLookupClient(
    HttpClient http,
    ICurrentUser currentUser,
    IOptions<VeteranLookupClientOptions> options,
    TimeProvider timeProvider,
    ILogger<VeteranLookupClient> logger) : IVeteranLookupClient
{
    /// <summary>Relative to the base address, so a base URL with a path prefix keeps it.</summary>
    internal const string LookupPath = "veterans/lookup";

    private static readonly VeteranLookupResult Forbidden = new(VeteranLookupOutcome.Forbidden, null, null);
    private static readonly VeteranLookupResult InvalidInput = new(VeteranLookupOutcome.InvalidInput, null, null);
    private static readonly VeteranLookupResult NotFound = new(VeteranLookupOutcome.NotFound, null, null);
    private static readonly VeteranLookupResult Unavailable = new(VeteranLookupOutcome.Unavailable, null, null);

    public async Task<VeteranLookupResult> LookupAsync(string ssn, CancellationToken ct)
    {
        var actor = await currentUser.GetAsync(ct);
        if (!actor.HasPermission(Permissions.VeteranLookup))
        {
            LookupLog.AuthorizationDenied(logger, actor.UserId, Permissions.VeteranLookup, null, nameof(LookupAsync));
            return Forbidden;
        }
        if (Normalize(ssn) is not { } normalized)
        {
            LookupLog.Rejected(logger);
            return InvalidInput;
        }

        var started = timeProvider.GetTimestamp();
        // The single owner of the total timeout: the pipeline bounds each attempt up to the response headers, and this
        // budget bounds the whole call including the body, so a stalled response cannot hold intake past it.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(options.Value.TotalTimeout);
        try
        {
            using var response = await http.PostAsJsonAsync(LookupPath, new LookupRequest(normalized), ClientJsonContext.Default.LookupRequest, budget.Token);
            return await MapAsync(response, normalized, started, budget.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TimeoutRejectedException or OperationCanceledException)
        {
            // A timeout surfaces as TimeoutRejectedException (last attempt) or OperationCanceledException (total
            // budget); either way the caller did not cancel. A connection dropped while the body is read is an IOException
            // (HttpIOException). Never log the exception message: it can embed request details.
            LookupLog.Unavailable(logger, ex.GetType().Name, null, Elapsed(started));
            return Unavailable;
        }
    }

    /// <summary>Accepts exactly nine ASCII digits or <c>NNN-NN-NNNN</c>; returns the dashed form, or null when invalid.</summary>
    internal static string? Normalize(string? ssn)
    {
        if (ssn is null || !SsnFormat().IsMatch(ssn))
        {
            return null;
        }
        return ssn.Length == 9 ? $"{ssn[..3]}-{ssn[3..5]}-{ssn[5..]}" : ssn;
    }

    private async Task<VeteranLookupResult> MapAsync(HttpResponseMessage response, string ssn, long started, CancellationToken ct)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                LookupLog.Completed(logger, nameof(VeteranLookupOutcome.NotFound), Elapsed(started));
                return NotFound;
            case HttpStatusCode.OK:
                LookupResponse? body;
                try
                {
                    body = await response.Content.ReadFromJsonAsync(ClientJsonContext.Default.LookupResponse, ct);
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
                {
                    body = null;   // malformed JSON, or a Content-Type/charset the reader cannot decode
                }

                // The result becomes the case's veteran snapshot, so it must satisfy the same rules intake applies. The
                // Government definition (4.6) leaves name and address optional; a 200 without both is Unavailable (D14).
                if (body is { Name: { } name, Address: { } address }
                    && CaseTextRules.ValidateVeteran(name, address, ssn[^CaseFieldLimits.SsnLast4Length..]).Count == 0)
                {
                    LookupLog.Completed(logger, nameof(VeteranLookupOutcome.Found), Elapsed(started));
                    return new VeteranLookupResult(VeteranLookupOutcome.Found, name, address);
                }

                LookupLog.Unavailable(logger, "UnusableResponse", (int)response.StatusCode, Elapsed(started));
                return Unavailable;
            default:
                LookupLog.Unavailable(logger, "UnexpectedStatus", (int)response.StatusCode, Elapsed(started));
                return Unavailable;
        }
    }

    private double Elapsed(long started) => timeProvider.GetElapsedTime(started).TotalMilliseconds;

    // \z, not $: "$" would also match before a trailing newline.
    [GeneratedRegex(@"^(?:[0-9]{9}|[0-9]{3}-[0-9]{2}-[0-9]{4})\z", RegexOptions.CultureInvariant)]
    private static partial Regex SsnFormat();
}

internal sealed record LookupRequest(string Ssn);

internal sealed record LookupResponse(string? Name, string? Address);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(LookupRequest))]
[JsonSerializable(typeof(LookupResponse))]
internal sealed partial class ClientJsonContext : JsonSerializerContext;
