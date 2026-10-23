using CaseIntake.Contracts.VeteranLookup;

namespace CaseIntake.TestKit.Fakes;

/// <summary>Scripted service-history lookup: register answers by 9-digit SSN; anything else is NotFound. Records calls without keeping SSNs in messages.</summary>
public sealed class FakeVeteranServiceHistoryClient : IVeteranServiceHistoryClient
{
    private readonly Dictionary<string, ServiceHistoryResult> _answers = [];

    public int CallCount { get; private set; }

    public FakeVeteranServiceHistoryClient Returns(string ssn, ServiceHistoryResult result)
    {
        _answers[ssn] = result;
        return this;
    }

    public Task<ServiceHistoryResult> LookupAsync(string ssn, CancellationToken ct)
    {
        CallCount++;
        var result = _answers.TryGetValue(ssn, out var found)
            ? found
            : new ServiceHistoryResult(VeteranLookupOutcome.NotFound, null);
        return Task.FromResult(result);
    }
}
