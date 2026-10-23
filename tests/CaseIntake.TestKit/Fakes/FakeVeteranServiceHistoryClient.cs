using CaseIntake.Contracts.VeteranLookup;

namespace CaseIntake.TestKit.Fakes;

/// <summary>Scripted service-history lookup: register answers by 9-digit SSN; anything else is NotFound. Records calls without keeping SSNs in messages.</summary>
public sealed class FakeVeteranServiceHistoryClient : IVeteranServiceHistoryClient
{
    private readonly Dictionary<string, ServiceHistoryResult> _answers = [];

    public int CallCount { get; private set; }

    /// <summary>The SSN of each call, in order (tests assert the same SSN as the lookup was used).</summary>
    public List<string> Requests { get; } = [];

    /// <summary>When set, answers every call instead of the registered answers: a pending task (busy state) or one that throws.</summary>
    public Func<string, CancellationToken, Task<ServiceHistoryResult>>? Respond { get; set; }

    public FakeVeteranServiceHistoryClient Returns(string ssn, ServiceHistoryResult result)
    {
        _answers[ssn] = result;
        return this;
    }

    public Task<ServiceHistoryResult> LookupAsync(string ssn, CancellationToken ct)
    {
        CallCount++;
        Requests.Add(ssn);
        if (Respond is { } respond)
        {
            return respond(ssn, ct);
        }
        var result = _answers.TryGetValue(ssn, out var found)
            ? found
            : new ServiceHistoryResult(VeteranLookupOutcome.NotFound, null);
        return Task.FromResult(result);
    }
}
