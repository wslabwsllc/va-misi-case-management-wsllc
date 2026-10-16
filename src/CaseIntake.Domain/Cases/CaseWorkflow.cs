using System.Collections.Frozen;
using System.Collections.Immutable;

namespace CaseIntake.Domain.Cases;

/// <summary>The controlled status workflow (spec 4.3). Every transition not listed is rejected.</summary>
public interface ICaseWorkflow
{
    bool IsAllowed(CaseStatus from, CaseStatus to);

    /// <summary>Statuses reachable from <paramref name="from"/> in one step; empty for Closed and undefined values.</summary>
    IReadOnlyList<CaseStatus> NextStatuses(CaseStatus from);
}

/// <summary>
/// Single source of truth for the allowed edges: New→Triaged, Triaged→InProgress, InProgress→Resolved,
/// Resolved→Closed and Resolved→InProgress (reopen). The UI receives the same edges through
/// <c>CaseDetailDto.AllowedNextStatuses</c>.
/// </summary>
public sealed class CaseWorkflow : ICaseWorkflow
{
    public static CaseWorkflow Instance { get; } = new();

    /// <summary>Status of every new case (adopted decision OQ-10).</summary>
    public const CaseStatus InitialStatus = CaseStatus.New;

    private static readonly FrozenDictionary<CaseStatus, ImmutableArray<CaseStatus>> Edges =
        new Dictionary<CaseStatus, ImmutableArray<CaseStatus>>
        {
            [CaseStatus.New] = [CaseStatus.Triaged],
            [CaseStatus.Triaged] = [CaseStatus.InProgress],
            [CaseStatus.InProgress] = [CaseStatus.Resolved],
            [CaseStatus.Resolved] = [CaseStatus.Closed, CaseStatus.InProgress],
            [CaseStatus.Closed] = [],
        }.ToFrozenDictionary();

    private CaseWorkflow()
    {
    }

    public bool IsAllowed(CaseStatus from, CaseStatus to) =>
        Edges.TryGetValue(from, out var next) && next.Contains(to);

    public IReadOnlyList<CaseStatus> NextStatuses(CaseStatus from) =>
        Edges.TryGetValue(from, out var next) ? next : ImmutableArray<CaseStatus>.Empty;

    /// <summary>Resolved→InProgress is audited as <see cref="AuditActions.Reopened"/> rather than a plain status change.</summary>
    public static bool IsReopen(CaseStatus from, CaseStatus to) =>
        from == CaseStatus.Resolved && to == CaseStatus.InProgress;
}
