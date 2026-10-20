using System.Collections.Immutable;
using CaseIntake.Domain.Cases;

namespace CaseIntake.Contracts.Security;

public static class AppRoles
{
    /// <summary>Read-only oversight: views cases, audit trails and documents, changes nothing.</summary>
    public const string Auditor = "Auditor";

    /// <summary>Front-door intake: opens cases, looks up the veteran and attaches documents; no editing or status changes.</summary>
    public const string IntakeClerk = "IntakeClerk";

    public const string CaseWorker = "CaseWorker";
    public const string Supervisor = "Supervisor";

    /// <summary>Quality assurance: reads cases and owns the test results and evidence module (E5 test results).</summary>
    public const string QualityAnalyst = "QualityAnalyst";

    /// <summary>The name shown to users; unknown roles are shown as stored.</summary>
    public static string DisplayName(string role) => role switch
    {
        IntakeClerk => "Intake clerk",
        CaseWorker => "Case worker",
        QualityAnalyst => "Quality analyst",
        _ => role,
    };

    /// <summary>The user's roles as display names in a stable order, or "no role".</summary>
    public static string Describe(IEnumerable<string> roles)
    {
        var names = roles.Order(StringComparer.Ordinal).Select(DisplayName).ToList();
        return names.Count == 0 ? "no role" : string.Join(", ", names);
    }
}

public static class Permissions
{
    public const string CasesView = "cases.view", CasesCreate = "cases.create", CasesEdit = "cases.edit";
    public const string AttachmentsUpload = "attachments.upload", AttachmentsDownload = "attachments.download";
    public const string VeteranLookup = "veteran.lookup";

    /// <summary>Assign or reassign the owner of any case (supervisors). Without it, a user who edits cases may only take an
    /// unassigned case for themselves or hand back their own.</summary>
    public const string CasesAssign = "cases.assign";

    /// <summary>View test results, evidence (screenshots, traces, logs) and exports.</summary>
    public const string TestResultsView = "testresults.view";

    /// <summary>Import executed test runs into the results store (runs are executed by the documented test command).</summary>
    public const string TestResultsManage = "testresults.manage";

    /// <summary>List users and add users of any role (supervisors).</summary>
    public const string UsersManage = "users.manage";

    public static string Transition(CaseStatus from, CaseStatus to) => $"cases.transition.{from}.{to}";
}

public sealed record OperationActor(string? UserId, string? DisplayName,
    ImmutableHashSet<string> Roles,
    ImmutableHashSet<string> Permissions)
{
    public bool IsAuthenticated => UserId is not null;
    public bool HasPermission(string permission) => IsAuthenticated && Permissions.Contains(permission);
}

public interface ICurrentUser
{
    Task<OperationActor> GetAsync(CancellationToken ct);
}

/// <summary>
/// Service keys for the explicit <see cref="ICurrentUser"/> adapters (E3-S3). The unkeyed registration is the circuit
/// adapter used by interactive components. HTTP endpoints (for example the attachment download) resolve the request
/// adapter with <c>[FromKeyedServices(CurrentUserSources.Http)] ICurrentUser</c> and pass it to a request-scoped
/// service instance; circuit identity is never inferred from <c>HttpContext</c>.
/// </summary>
public static class CurrentUserSources
{
    public const string Http = "http";
}

/// <summary>
/// The shared <c>AuthorizationDenied</c> warning event (E3-S5). Every module logs an authorization denial with this
/// event ID and name, with the user, the permission and the resource ID, so denials are found with one query.
/// </summary>
public static class SecurityEvents
{
    public const int AuthorizationDeniedId = 3005;
    public const string AuthorizationDeniedName = "AuthorizationDenied";
}
