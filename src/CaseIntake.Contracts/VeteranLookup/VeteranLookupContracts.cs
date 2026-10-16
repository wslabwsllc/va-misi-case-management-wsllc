namespace CaseIntake.Contracts.VeteranLookup;

public enum VeteranLookupOutcome { Found, NotFound, InvalidInput, Unavailable, Forbidden }
public sealed record VeteranLookupResult(VeteranLookupOutcome Outcome, string? Name, string? Address);

public interface IVeteranLookupClient
{
    Task<VeteranLookupResult> LookupAsync(string ssn, CancellationToken ct);
}
