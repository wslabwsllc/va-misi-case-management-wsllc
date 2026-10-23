using System.Collections.Frozen;
using System.Collections.Immutable;
using CaseIntake.Domain.Cases;

namespace CaseIntake.Contracts.Security;

/// <summary>One permission as administrators see it. <paramref name="Name"/> is the policy name (<see cref="Permissions"/>).</summary>
public sealed record PermissionDefinition(string Name, string Group, string Label, string Description);

/// <summary>
/// Describes every permission in <see cref="RolePermissionMap.AllPermissions"/> (same order). The seed copies it into the
/// <c>Permissions</c> table, which role and user grants reference; the Roles and Users pages group checkboxes by
/// <see cref="PermissionDefinition.Group"/>.
/// </summary>
public static class PermissionCatalog
{
    public const string CasesGroup = "Cases", WorkflowGroup = "Case workflow", DocumentsGroup = "Documents and lookup",
        TestResultsGroup = "Test results", AdministrationGroup = "Administration";

    public static ImmutableArray<PermissionDefinition> All { get; } =
    [
        new(Permissions.CasesView, CasesGroup, "View cases", "See cases, their history and the dashboards."),
        new(Permissions.CasesCreate, CasesGroup, "Create cases", "Open new cases."),
        new(Permissions.CasesEdit, CasesGroup, "Edit cases", "Change case details, take an unassigned case or hand back their own."),
        new(Permissions.VeteranLookup, DocumentsGroup, "Look up veterans", "Search the veteran lookup service when opening a case."),
        new(Permissions.AttachmentsUpload, DocumentsGroup, "Upload documents", "Attach documents to cases."),
        new(Permissions.AttachmentsDownload, DocumentsGroup, "Download documents", "Open and download case documents."),
        new(Permissions.CasesAssign, CasesGroup, "Assign cases", "Assign or reassign the owner of any case."),
        new(Permissions.Transition(CaseStatus.New, CaseStatus.Triaged), WorkflowGroup, "Triage", "Move a case from New to Triaged."),
        new(Permissions.Transition(CaseStatus.Triaged, CaseStatus.InProgress), WorkflowGroup, "Start work", "Move a case from Triaged to In progress."),
        new(Permissions.Transition(CaseStatus.InProgress, CaseStatus.Resolved), WorkflowGroup, "Resolve", "Move a case from In progress to Resolved."),
        new(Permissions.Transition(CaseStatus.Resolved, CaseStatus.Closed), WorkflowGroup, "Close", "Move a case from Resolved to Closed."),
        new(Permissions.Transition(CaseStatus.Resolved, CaseStatus.InProgress), WorkflowGroup, "Reopen", "Move a case from Resolved back to In progress."),
        new(Permissions.TestResultsView, TestResultsGroup, "View test results", "See test runs, evidence and exports."),
        new(Permissions.TestResultsManage, TestResultsGroup, "Import test results", "Import executed test runs."),
        new(Permissions.UsersManage, AdministrationGroup, "Manage users", "Add and edit users, their roles, status and permission overrides."),
        new(Permissions.RolesManage, AdministrationGroup, "Manage roles", "Create, edit and delete roles and choose their permissions."),
        new(Permissions.AuditView, AdministrationGroup, "View audit log", "Read the audit log, open entries and export them; see the history of users and roles."),
        new(Permissions.ErrorsView, AdministrationGroup, "View errors", "Read the error log, an error's details and stack trace."),
        new(Permissions.ErrorsManage, AdministrationGroup, "Manage errors", "Acknowledge, resolve and reopen errors."),
        new(Permissions.SystemHealth, AdministrationGroup, "View system health", "See health checks, database and migration status, and error and security counts."),
    ];

    private static readonly FrozenDictionary<string, PermissionDefinition> ByName =
        All.ToFrozenDictionary(p => p.Name, StringComparer.Ordinal);

    /// <summary>The groups in display order.</summary>
    public static ImmutableArray<string> Groups { get; } = [.. All.Select(p => p.Group).Distinct()];

    public static bool Contains(string permission) => ByName.ContainsKey(permission);

    /// <summary>The definition, or <c>null</c> for a name the catalog does not define.</summary>
    public static PermissionDefinition? Find(string permission) => ByName.GetValueOrDefault(permission);

    /// <summary>The label shown to people, or the raw name for an unknown permission.</summary>
    public static string Label(string permission) => Find(permission)?.Label ?? permission;
}

public enum PermissionEffect { Allow, Deny }

/// <summary>
/// The effective-permission rule (ADR 0013): the union of the user's role permissions, plus the permissions allowed for the
/// user, minus the permissions denied for the user. Deny wins, except that <see cref="Permissions.CasesCreate"/> always
/// brings <see cref="Permissions.VeteranLookup"/> (intake requires a veteran). An inactive user holds nothing.
/// </summary>
public static class EffectivePermissions
{
    public static ImmutableHashSet<string> Compute(bool isActive, IEnumerable<string> rolePermissions,
        IReadOnlyDictionary<string, PermissionEffect> overrides)
    {
        ArgumentNullException.ThrowIfNull(rolePermissions);
        ArgumentNullException.ThrowIfNull(overrides);
        if (!isActive)
        {
            return ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
        }

        var result = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        result.UnionWith(rolePermissions);
        foreach (var (permission, effect) in overrides)
        {
            if (effect == PermissionEffect.Allow)
            {
                result.Add(permission);
            }
        }
        foreach (var (permission, effect) in overrides)
        {
            if (effect == PermissionEffect.Deny)
            {
                result.Remove(permission);
            }
        }
        // A case cannot be created without a veteran lookup, so creating always grants it (even over a Deny).
        if (result.Contains(Permissions.CasesCreate))
        {
            result.Add(Permissions.VeteranLookup);
        }
        return result.ToImmutable();
    }
}
