using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;

namespace CaseIntake.Contracts.Cases;

/// <summary>Sets the owner of a case. A null or empty <paramref name="AssignedTo"/> makes the case unassigned.</summary>
public sealed record AssignCaseRequest(string? AssignedTo, uint Version);   // Version = optimistic concurrency token

/// <summary>Case assignment: the only way to change who owns a case (issue #22 follow-up).</summary>
public interface ICaseAssignmentService
{
    Task<Result<CaseDetailDto>> AssignAsync(long id, AssignCaseRequest request, CancellationToken ct);
}

/// <summary>What a user may do with the owner of a case.</summary>
public enum AssignmentRight
{
    None,

    /// <summary>Assign or reassign to anyone who works cases, or unassign (<c>cases.assign</c>, supervisors).</summary>
    AssignAnyone,

    /// <summary>Take an unassigned case for themselves (<c>cases.edit</c> without <c>cases.assign</c>).</summary>
    Take,

    /// <summary>Hand back a case they own, leaving it unassigned (<c>cases.edit</c> without <c>cases.assign</c>).</summary>
    HandBack,
}

/// <summary>
/// The assignment rules, shared by the case service (which enforces them) and the UI (which only offers what they
/// allow): supervisors assign anyone; someone who edits cases takes an unassigned case or hands back their own; nobody
/// changes the owner of a Closed case. <paramref name="me"/> is the actor's user name as cases record it.
/// </summary>
public static class CaseAssignmentRules
{
    public static AssignmentRight RightFor(OperationActor actor, string? me, CaseStatus status, string? currentOwner)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (status == CaseStatus.Closed || !actor.IsAuthenticated)
        {
            return AssignmentRight.None;
        }
        if (actor.HasPermission(Permissions.CasesAssign))
        {
            return AssignmentRight.AssignAnyone;
        }
        if (!actor.HasPermission(Permissions.CasesEdit) || string.IsNullOrEmpty(me))
        {
            return AssignmentRight.None;
        }
        return currentOwner is null ? AssignmentRight.Take
            : string.Equals(currentOwner, me, StringComparison.Ordinal) ? AssignmentRight.HandBack
            : AssignmentRight.None;
    }

    /// <summary>True when <paramref name="right"/> covers moving the owner from <paramref name="current"/> to <paramref name="target"/>.</summary>
    public static bool Allows(AssignmentRight right, string? me, string? current, string? target) => right switch
    {
        AssignmentRight.AssignAnyone => true,
        AssignmentRight.Take => current is null && string.Equals(target, me, StringComparison.Ordinal),
        AssignmentRight.HandBack => target is null && string.Equals(current, me, StringComparison.Ordinal),
        _ => false,
    };
}
