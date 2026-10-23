using CaseIntake.Domain.Cases;

namespace CaseIntake.Contracts.VeteranLookup;

public enum VeteranLookupOutcome { Found, NotFound, InvalidInput, Unavailable, Forbidden }
public sealed record VeteranLookupResult(VeteranLookupOutcome Outcome, string? Name, string? Address);

public interface IVeteranLookupClient
{
    Task<VeteranLookupResult> LookupAsync(string ssn, CancellationToken ct);
}

/// <summary>Service-history lookup result. Reuses <see cref="VeteranLookupOutcome"/>; callers store anything other than
/// Found or NotFound as <see cref="ServiceHistoryStatus.Unavailable"/> (D6).</summary>
public sealed record ServiceHistoryResult(VeteranLookupOutcome Outcome, ServiceHistory? History);

public interface IVeteranServiceHistoryClient
{
    Task<ServiceHistoryResult> LookupAsync(string ssn, CancellationToken ct);
}
