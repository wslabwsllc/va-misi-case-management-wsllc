using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using Polly.Timeout;

namespace CaseIntake.VeteranLookup.Client;

/// <summary>
/// The transport both clients share (ADR 0018): one POST under the client's total budget, with every transport failure
/// turned into the caller's "unavailable" result. Only the response mapping differs between the Lookup and the Service
/// History client. The budget is applied here, not in the pipeline, so it also covers reading the response body; the
/// pipeline bounds each attempt up to the response headers. Only the caller's own cancellation propagates.
/// </summary>
internal static class LookupTransport
{
    /// <param name="map">Maps a response to the result; runs under the same budget, so a stalled body times out.</param>
    /// <param name="unavailable">Builds (and logs) the result for a failure, given the exception type name. Never given
    /// the exception message: it can embed request details.</param>
    public static async Task<TResult> PostAsync<TRequest, TResult>(
        HttpClient http,
        string path,
        TRequest body,
        JsonTypeInfo<TRequest> bodyType,
        TimeSpan totalTimeout,
        Func<HttpResponseMessage, CancellationToken, Task<TResult>> map,
        Func<string, TResult> unavailable,
        CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(totalTimeout);
        try
        {
            using var response = await http.PostAsJsonAsync(path, body, bodyType, budget.Token);
            return await map(response, budget.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TimeoutRejectedException or OperationCanceledException)
        {
            // A timeout surfaces as TimeoutRejectedException (last attempt) or OperationCanceledException (total
            // budget); either way the caller did not cancel. A connection dropped while the body is read is an IOException
            // (HttpIOException).
            return unavailable(ex.GetType().Name);
        }
    }
}
