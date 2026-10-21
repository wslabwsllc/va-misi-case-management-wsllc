using CaseIntake.Contracts.Auditing;
using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Diagnostics;
using CaseIntake.Contracts.Security;

namespace CaseIntake.Web.Tests;

/// <summary>
/// Users held in memory with the default matrix for role permissions. <see cref="NextError"/> makes the next change fail
/// with that error; every request is recorded. Stamps change on every successful change, like Identity's.
/// </summary>
public sealed class FakeUserAdministrationService : IUserAdministrationService
{
    private int _stamp;

    public List<UserDetailDto> Users { get; } = [Detail("1", "supervisor@example.test", [AppRoles.Supervisor])];

    /// <summary>Every role; Administrator cannot be granted by the default (supervisor) actor.</summary>
    public List<RoleOptionDto> RoleOptions { get; } =
        [.. RolePermissionMap.Roles.Select(r => new RoleOptionDto(r, $"{r} description", r != AppRoles.Administrator))];

    public List<object> Requests { get; } = [];
    public List<UserListQuery> Queries { get; } = [];
    public Error? NextError { get; set; }
    /// <summary>Thrown by the next create.</summary>
    public Exception? NextException { get; set; }
    /// <summary>Returned (or thrown) by every list call while set.</summary>
    public Error? ListError { get; set; }
    public Exception? ListException { get; set; }

    public static UserDetailDto Detail(string id, string userName, IReadOnlyList<string> roles, bool isActive = true,
        IReadOnlyDictionary<string, PermissionEffect>? overrides = null, bool canManage = true, string? reason = null,
        DateTimeOffset? lockoutEndUtc = null, string stamp = "stamp-0")
    {
        overrides ??= new Dictionary<string, PermissionEffect>();
        var fromRoles = RolePermissionMap.PermissionsFor(roles);
        var effective = EffectivePermissions.Compute(isActive, fromRoles, overrides);
        return new UserDetailDto(id, userName, isActive, lockoutEndUtc, new DateTimeOffset(2026, 10, 1, 14, 0, 0, TimeSpan.Zero),
            null, isActive ? null : new DateTimeOffset(2026, 10, 2, 14, 0, 0, TimeSpan.Zero), roles, overrides,
            [.. PermissionCatalog.All.Select(p => new EffectivePermissionDto(p.Name, effective.Contains(p.Name),
                [.. roles.Where(r => RolePermissionMap.Grants([r], p.Name))], overrides.TryGetValue(p.Name, out var e) ? e : null))],
            stamp, canManage, reason);
    }

    public Task<Result<PagedResult<UserSummaryDto>>> ListAsync(UserListQuery query, CancellationToken ct)
    {
        Queries.Add(query);
        if (ListException is { } thrown)
        {
            throw thrown;
        }
        if (ListError is { } failure)
        {
            return Task.FromResult(Result<PagedResult<UserSummaryDto>>.Failure(failure));
        }
        var matches = Users
            .Where(u => query.Search is null || u.UserName.Contains(query.Search, StringComparison.OrdinalIgnoreCase))
            .Where(u => query.Role is null || u.Roles.Contains(query.Role))
            .Where(u => query.Status switch { UserStatusFilter.Active => u.IsActive, UserStatusFilter.Inactive => !u.IsActive, _ => true })
            .OrderBy(u => u.UserName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var page = matches.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(u => new UserSummaryDto(u.Id, u.UserName, u.Roles, u.IsActive, u.LockoutEndUtc is not null, u.LastSignInUtc)).ToList();
        return Task.FromResult(Result<PagedResult<UserSummaryDto>>.Success(new(page, query.Page, query.PageSize, matches.Count)));
    }

    public Task<Result<IReadOnlyList<RoleOptionDto>>> ListRoleOptionsAsync(CancellationToken ct) =>
        Task.FromResult(Result<IReadOnlyList<RoleOptionDto>>.Success([.. RoleOptions]));

    public Task<Result<UserSummaryDto>> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        if (NextException is { } ex)
        {
            NextException = null;
            throw ex;
        }
        if (TakeError() is { } error)
        {
            return Task.FromResult(Result<UserSummaryDto>.Failure(error));
        }
        var user = Detail($"{Users.Count + 1}", request.Email, request.Roles);
        Users.Add(user);
        return Task.FromResult(Result<UserSummaryDto>.Success(new UserSummaryDto(user.Id, user.UserName, user.Roles)));
    }

    public Task<Result<UserDetailDto>> GetAsync(string id, CancellationToken ct) =>
        Task.FromResult(Users.FirstOrDefault(u => u.Id == id) is { } user
            ? Result<UserDetailDto>.Success(user)
            : Result<UserDetailDto>.Failure(new(ErrorKind.NotFound, "UserNotFound", "This user no longer exists.")));

    public Task<Result<UserDetailDto>> UpdateAsync(string id, UpdateUserRequest request, CancellationToken ct) =>
        Change(id, request, u => Detail(u.Id, request.Email, request.Roles, u.IsActive, u.Overrides, stamp: NextStamp()));

    public Task<Result<UserDetailDto>> SetStatusAsync(string id, SetUserStatusRequest request, CancellationToken ct) =>
        Change(id, request, u => Detail(u.Id, u.UserName, u.Roles, request.Active, u.Overrides, stamp: NextStamp()));

    public Task<Result<UserDetailDto>> SetOverridesAsync(string id, SetPermissionOverridesRequest request, CancellationToken ct) =>
        Change(id, request, u => Detail(u.Id, u.UserName, u.Roles, u.IsActive, new Dictionary<string, PermissionEffect>(request.Overrides), stamp: NextStamp()));

    public Task<Result<UserDetailDto>> UnlockAsync(string id, string concurrencyStamp, CancellationToken ct) =>
        Change(id, concurrencyStamp, u => Detail(u.Id, u.UserName, u.Roles, u.IsActive, u.Overrides, stamp: NextStamp()));

    private Task<Result<UserDetailDto>> Change(string id, object request, Func<UserDetailDto, UserDetailDto> apply)
    {
        Requests.Add(request);
        if (TakeError() is { } error)
        {
            return Task.FromResult(Result<UserDetailDto>.Failure(error));
        }
        var index = Users.FindIndex(u => u.Id == id);
        if (index < 0)
        {
            return GetAsync(id, CancellationToken.None);
        }
        Users[index] = apply(Users[index]);
        return Task.FromResult(Result<UserDetailDto>.Success(Users[index]));
    }

    private string NextStamp() => $"stamp-{++_stamp}";

    private Error? TakeError()
    {
        var error = NextError;
        NextError = null;
        return error;
    }
}

/// <summary>Roles held in memory (the built-in roles with default permissions); records requests; <see cref="NextError"/> fails the next change.</summary>
public sealed class FakeRoleAdministrationService : IRoleAdministrationService
{
    public List<RoleDetailDto> Roles { get; } = [.. RolePermissionMap.Roles.Select(r => new RoleDetailDto(
        $"role-{r.ToLowerInvariant()}", r, $"{r} description", true, r == AppRoles.Administrator,
        [.. RolePermissionMap.AllPermissions.Where(p => RolePermissionMap.Grants([r], p))], [], "stamp-0",
        r != AppRoles.Administrator, r == AppRoles.Administrator ? "The Administrator role always has every permission and cannot be changed." : null))];

    public List<object> Requests { get; } = [];
    public Error? NextError { get; set; }

    public Task<Result<IReadOnlyList<RoleSummaryDto>>> ListAsync(CancellationToken ct) =>
        Task.FromResult(Result<IReadOnlyList<RoleSummaryDto>>.Success([.. Roles.Select(r =>
            new RoleSummaryDto(r.Id, r.Name, r.Description, r.IsBuiltIn, r.IsLocked, r.Members.Count, r.Permissions))]));

    public Task<Result<RoleDetailDto>> GetAsync(string id, CancellationToken ct) =>
        Task.FromResult(Roles.FirstOrDefault(r => r.Id == id) is { } role
            ? Result<RoleDetailDto>.Success(role)
            : Result<RoleDetailDto>.Failure(new(ErrorKind.NotFound, "RoleNotFound", "This role no longer exists.")));

    public Task<Result<RoleDetailDto>> CreateAsync(SaveRoleRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        if (TakeError() is { } error)
        {
            return Task.FromResult(Result<RoleDetailDto>.Failure(error));
        }
        var role = new RoleDetailDto($"custom-{Roles.Count}", request.Name, request.Description, false, false, request.Permissions, [], "stamp-0", true, null);
        Roles.Add(role);
        return Task.FromResult(Result<RoleDetailDto>.Success(role));
    }

    public Task<Result<RoleDetailDto>> UpdateAsync(string id, SaveRoleRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        if (TakeError() is { } error)
        {
            return Task.FromResult(Result<RoleDetailDto>.Failure(error));
        }
        var index = Roles.FindIndex(r => r.Id == id);
        Roles[index] = Roles[index] with
        {
            Name = Roles[index].IsBuiltIn ? Roles[index].Name : request.Name,
            Description = request.Description,
            Permissions = request.Permissions,
            ConcurrencyStamp = $"stamp-{Requests.Count}",
        };
        return Task.FromResult(Result<RoleDetailDto>.Success(Roles[index]));
    }

    public Task<Result<string>> DeleteAsync(string id, string concurrencyStamp, CancellationToken ct)
    {
        Requests.Add(id);
        if (TakeError() is { } error)
        {
            return Task.FromResult(Result<string>.Failure(error));
        }
        var role = Roles.Single(r => r.Id == id);
        Roles.Remove(role);
        return Task.FromResult(Result<string>.Success(role.Name));
    }

    private Error? TakeError()
    {
        var error = NextError;
        NextError = null;
        return error;
    }
}

/// <summary>Audit entries held in memory, filtered like the real service (subject, category, outcome, text); records queries.</summary>
public sealed class FakeAuditLogService : IAuditLogService
{
    public List<AuditLogEntryDto> Entries { get; } = [];
    public List<AuditQuery> Queries { get; } = [];
    public List<AuditQuery> Exports { get; } = [];

    public static AuditLogEntryDto Entry(string key, string action, string category = AuditCategories.Users, AuditOutcome outcome = AuditOutcome.Success,
        string? entityType = AuditEntityTypes.User, string? entityId = "7", string? summary = null, IReadOnlyList<AuditChangeDto>? changes = null,
        DateTimeOffset? at = null) =>
        new(key, at ?? new DateTimeOffset(2026, 10, 21, 13, 0, 0, TimeSpan.Zero), "admin-id", "admin@example.test", category, action, outcome,
            entityType, entityId, entityId is null ? null : $"subject {entityId}", summary, "op-1", changes ?? []);

    public Task<Result<PagedResult<AuditLogEntryDto>>> ListAsync(AuditQuery query, CancellationToken ct)
    {
        Queries.Add(query);
        var matches = Entries
            .Where(e => query.EntityType is null || e.EntityType == query.EntityType)
            .Where(e => query.EntityId is null || e.EntityId == query.EntityId)
            .Where(e => query.Category is null || e.Category == query.Category)
            .Where(e => query.Outcome is null || e.Outcome == query.Outcome)
            .Where(e => query.Search is null || e.Action.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                || (e.Summary?.Contains(query.Search, StringComparison.OrdinalIgnoreCase) ?? false) || e.CorrelationId == query.Search)
            .OrderByDescending(e => e.OccurredAtUtc)
            .ToList();
        return Task.FromResult(Result<PagedResult<AuditLogEntryDto>>.Success(new(
            [.. matches.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)], query.Page, query.PageSize, matches.Count)));
    }

    public Task<Result<AuditLogEntryDto>> GetAsync(string key, CancellationToken ct) =>
        Task.FromResult(Entries.FirstOrDefault(e => e.Key == key) is { } entry
            ? Result<AuditLogEntryDto>.Success(entry)
            : Result<AuditLogEntryDto>.Failure(new(ErrorKind.NotFound, "AuditEntryNotFound", "This audit entry does not exist.")));

    public Task<Result<AuditExport>> ExportAsync(AuditQuery query, CancellationToken ct)
    {
        Exports.Add(query);
        return Task.FromResult(Result<AuditExport>.Success(new AuditExport("audit.csv", [], 0, false)));
    }
}

/// <summary>Errors held in memory; status changes check the version and bump it. Records requests.</summary>
public sealed class FakeErrorLogService : IErrorLogService
{
    public List<ErrorDetailDto> Errors { get; } = [];
    public List<object> Requests { get; } = [];

    public static ErrorDetailDto Error(long id, ErrorStatus status = ErrorStatus.Open, ErrorLevel level = ErrorLevel.Error, long version = 1) =>
        new(id, new DateTimeOffset(2026, 10, 21, 13, 0, 0, TimeSpan.Zero), level, "CaseIntake.Cases.CaseService", 2050, "SaveFailed",
            "Saving case 12 failed", "System.InvalidOperationException", "System.InvalidOperationException\n   at CaseIntake.Cases.CaseService.SaveAsync()",
            "op-err", "/cases/12", "0123456789abcdef", 3, status, null, null, null, version);

    private static ErrorSummaryDto Summary(ErrorDetailDto e) =>
        new(e.Id, e.OccurredAtUtc, e.Level, e.Category, e.Message, e.ExceptionType, e.Status, e.CorrelationId, e.Fingerprint);

    public Task<Result<PagedResult<ErrorSummaryDto>>> ListAsync(ErrorQuery query, CancellationToken ct)
    {
        Requests.Add(query);
        var matches = Errors.Where(e => query.Status is null || e.Status == query.Status)
            .Where(e => query.Fingerprint is null || e.Fingerprint == query.Fingerprint).Select(Summary).ToList();
        return Task.FromResult(Result<PagedResult<ErrorSummaryDto>>.Success(new(matches, query.Page, query.PageSize, matches.Count)));
    }

    public Task<Result<ErrorDetailDto>> GetAsync(long id, CancellationToken ct) =>
        Task.FromResult(Errors.FirstOrDefault(e => e.Id == id) is { } e
            ? Result<ErrorDetailDto>.Success(e)
            : Result<ErrorDetailDto>.Failure(new(ErrorKind.NotFound, "ErrorNotFound", "This error does not exist.")));

    public Task<Result<IReadOnlyList<ErrorSummaryDto>>> FindByReferenceAsync(string reference, CancellationToken ct) =>
        Task.FromResult(Result<IReadOnlyList<ErrorSummaryDto>>.Success([.. Errors.Where(e => e.CorrelationId == reference).Select(Summary)]));

    public Task<Result<ErrorDetailDto>> SetStatusAsync(long id, SetErrorStatusRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        var index = Errors.FindIndex(e => e.Id == id);
        if (Errors[index].Version != request.Version)
        {
            return Task.FromResult(Result<ErrorDetailDto>.Failure(new(ErrorKind.Conflict, "Conflict", "Someone else changed this error.")));
        }
        Errors[index] = Errors[index] with
        {
            Status = request.Status, StatusNote = request.Note, StatusChangedBy = "admin@example.test",
            StatusChangedUtc = new DateTimeOffset(2026, 10, 21, 14, 0, 0, TimeSpan.Zero), Version = request.Version + 1,
        };
        return Task.FromResult(Result<ErrorDetailDto>.Success(Errors[index]));
    }
}

/// <summary>A fixed System Health result; <see cref="Next"/> replaces it.</summary>
public sealed class FakeSystemHealthService : ISystemHealthService
{
    public int Calls { get; private set; }

    public SystemHealthDto Next { get; set; } = new(new DateTimeOffset(2026, 10, 21, 16, 0, 0, TimeSpan.Zero), HealthState.Degraded,
        [new HealthCheckDto("database", HealthState.Healthy, null, TimeSpan.FromMilliseconds(12)),
         new HealthCheckDto("veteran-lookup", HealthState.Degraded, "Slow to answer", TimeSpan.FromMilliseconds(900))],
        "1.4.0+abc", "Production", new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero), true, 8, [], 2,
        [new CountDto("Error", 4), new CountDto("Critical", 0)],
        [new CountDto(AuditLogActions.SignInFailed, 3), new CountDto(AuditLogActions.SignInLockedOut, 0), new CountDto(AuditLogActions.AccessDenied, 1)],
        []);

    public Task<Result<SystemHealthDto>> GetAsync(CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(Result<SystemHealthDto>.Success(Next));
    }
}
