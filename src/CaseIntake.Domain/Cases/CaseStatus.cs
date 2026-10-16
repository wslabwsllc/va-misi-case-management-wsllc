namespace CaseIntake.Domain.Cases;

/// <summary>The five case statuses. Numeric values are persisted; never renumber.</summary>
public enum CaseStatus
{
    New = 0,
    Triaged = 1,
    InProgress = 2,
    Resolved = 3,
    Closed = 4,
}
