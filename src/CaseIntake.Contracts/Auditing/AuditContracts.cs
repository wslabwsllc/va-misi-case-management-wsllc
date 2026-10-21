using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;

namespace CaseIntake.Contracts.Auditing;

/// <summary>The audit log's categories. Stored as text; never rename an existing value.</summary>
public static class AuditCategories
{
    public const string Authentication = "Authentication";
    public const string Authorization = "Authorization";
    public const string Users = "Users";
    public const string Roles = "Roles";
    public const string Cases = "Cases";
    public const string Attachments = "Attachments";
    public const string TestResults = "TestResults";
    public const string AuditLog = "AuditLog";
    public const string Errors = "Errors";

    public static IReadOnlyList<string> All { get; } =
        [Authentication, Authorization, Users, Roles, Cases, Attachments, TestResults, AuditLog, Errors];

    public static string DisplayName(string category) => category switch
    {
        TestResults => "Test results",
        AuditLog => "Audit log",
        _ => category,
    };
}

/// <summary>Actions recorded in <c>AuditLogEntries</c>. Stored as text; never rename an existing value.</summary>
public static class AuditLogActions
{
    public const string SignedIn = "SignedIn", SignInFailed = "SignInFailed", SignInLockedOut = "SignInLockedOut",
        SignedOut = "SignedOut", PasswordChanged = "PasswordChanged", PasswordChangeFailed = "PasswordChangeFailed";

    public const string AccessDenied = "AccessDenied";

    public const string UserCreated = "UserCreated", UserUpdated = "UserUpdated", UserDeactivated = "UserDeactivated",
        UserReactivated = "UserReactivated", UserPermissionsChanged = "UserPermissionsChanged", UserUnlocked = "UserUnlocked";

    public const string RoleCreated = "RoleCreated", RoleUpdated = "RoleUpdated", RoleDeleted = "RoleDeleted";

    public const string AuditExported = "AuditExported";

    public const string ErrorStatusChanged = "ErrorStatusChanged";
}

/// <summary>Entity types an audit entry can be about.</summary>
public static class AuditEntityTypes
{
    public const string User = "User", Role = "Role", Case = "Case", TestRun = "TestRun", Page = "Page", Error = "Error";
}

public enum AuditOutcome { Success, Failure, Denied }

/// <summary>
/// One event to record in <c>AuditLogEntries</c>. <see cref="Before"/> and <see cref="After"/> hold only safe field
/// values (names, IDs, flags, role and permission names); never passwords, hashes, tokens, SSNs, veteran details or
/// free text a user typed into a case. <see cref="ActorId"/> is null for an unknown or anonymous actor.
/// </summary>
public sealed record AuditRecord(
    string Category,
    string Action,
    AuditOutcome Outcome,
    string? ActorId,
    string? ActorName,
    string? EntityType = null,
    string? EntityId = null,
    string? EntityName = null,
    string? Summary = null,
    IReadOnlyDictionary<string, string?>? Before = null,
    IReadOnlyDictionary<string, string?>? After = null);

/// <summary>
/// Audit log query. Every filter is optional and they combine with AND. <paramref name="FromDate"/> and
/// <paramref name="ToDate"/> are inclusive Eastern calendar days; <paramref name="Actor"/> matches part of the actor's
/// name; <paramref name="Search"/> matches part of the action, entity, summary or correlation ID.
/// </summary>
public sealed record AuditQuery(
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    string? Category = null,
    string? Actor = null,
    string? EntityType = null,
    string? EntityId = null,
    AuditOutcome? Outcome = null,
    string? Search = null,
    bool OldestFirst = false,
    int Page = 1,
    int PageSize = 50)
{
    public const int MaxPageSize = 200;

    /// <summary>The most rows one CSV export returns; narrow the filters for more.</summary>
    public const int MaxExportRows = 10_000;

    public const int SearchMaxLength = 200;
}

/// <summary>One field that changed: its value before and after (either may be empty).</summary>
public sealed record AuditChangeDto(string Field, string? Before, string? After);

/// <summary>
/// One audit entry from any source. <see cref="Key"/> identifies it across sources (<c>A-</c> audit log, <c>C-</c> case
/// trail, <c>T-</c> test results trail).
/// </summary>
public sealed record AuditLogEntryDto(
    string Key,
    DateTimeOffset OccurredAtUtc,
    string? ActorId,
    string? ActorName,
    string Category,
    string Action,
    AuditOutcome Outcome,
    string? EntityType,
    string? EntityId,
    string? EntityName,
    string? Summary,
    string? CorrelationId,
    IReadOnlyList<AuditChangeDto> Changes);

/// <summary>A CSV export: the file name and its UTF-8 content, plus whether the row cap cut it short.</summary>
public sealed record AuditExport(string FileName, byte[] Content, int RowCount, bool Truncated);

/// <summary>Reads the system-wide audit log (<c>audit.view</c>). Every read requires the permission.</summary>
public interface IAuditLogService
{
    Task<Result<PagedResult<AuditLogEntryDto>>> ListAsync(AuditQuery query, CancellationToken ct);

    Task<Result<AuditLogEntryDto>> GetAsync(string key, CancellationToken ct);

    /// <summary>CSV (UTC times, like every export) of the entries the query matches, newest first, up to <see cref="AuditQuery.MaxExportRows"/>. The export itself is audited.</summary>
    Task<Result<AuditExport>> ExportAsync(AuditQuery query, CancellationToken ct);
}
