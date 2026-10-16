using CaseIntake.Contracts.VeteranLookup;

namespace CaseIntake.TestKit.Fakes;

/// <summary>Scripted lookup: register answers by 9-digit SSN; anything else is NotFound. Records calls without keeping SSNs in messages.</summary>
public sealed class FakeVeteranLookupClient : IVeteranLookupClient
{
    private readonly Dictionary<string, VeteranLookupResult> _answers = [];

    public int CallCount { get; private set; }

    public FakeVeteranLookupClient Returns(string ssn, VeteranLookupResult result)
    {
        _answers[ssn] = result;
        return this;
    }

    public Task<VeteranLookupResult> LookupAsync(string ssn, CancellationToken ct)
    {
        CallCount++;
        var result = _answers.TryGetValue(ssn, out var found)
            ? found
            : new VeteranLookupResult(VeteranLookupOutcome.NotFound, null, null);
        return Task.FromResult(result);
    }
}
