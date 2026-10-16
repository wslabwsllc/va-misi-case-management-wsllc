using System.Collections.Immutable;
using CaseIntake.Domain.Cases;

namespace CaseIntake.Contracts.Security;

public static class AppRoles
{
    public const string CaseWorker = "CaseWorker";
    public const string Supervisor = "Supervisor";
}

public static class Permissions
{
    public const string CasesView = "cases.view", CasesCreate = "cases.create", CasesEdit = "cases.edit";
    public const string AttachmentsUpload = "attachments.upload", AttachmentsDownload = "attachments.download";
    public const string VeteranLookup = "veteran.lookup";
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
