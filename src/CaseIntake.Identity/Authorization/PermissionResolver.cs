using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security.Claims;
using CaseIntake.Contracts.Security;
using CaseIntake.Identity.CurrentUser;
using CaseIntake.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CaseIntake.Identity.Authorization;

/// <summary>
/// Resolves a principal to its <see cref="OperationActor"/> from the stored role permissions and the user's stored access
/// (ADR 0013). Singleton with a short cache: the role map and each user's access are kept for at most
/// <see cref="IdentityModuleOptions.RevalidationInterval"/>, and the administration services call <see cref="Invalidate"/>
/// after every change, so this instance applies a change at the next check. A load that started before an invalidation
/// is never cached.
/// </summary>
public sealed class PermissionResolver(
    IPermissionStore store,
    TimeProvider time,
    IOptions<IdentityModuleOptions> options)
{
    private sealed record Cached<T>(T Value, DateTimeOffset LoadedAt);

    private readonly ConcurrentDictionary<string, Cached<StoredUserAccess?>> _users = new(StringComparer.Ordinal);
    private Cached<IReadOnlyDictionary<string, ImmutableHashSet<string>>>? _roles;
    private long _generation;

    public async Task<OperationActor> ResolveAsync(ClaimsPrincipal? principal, CancellationToken ct)
    {
        if (OperationActorFactory.UserId(principal) is not { } userId)
        {
            return OperationActorFactory.Anonymous;
        }

        var generation = Interlocked.Read(ref _generation);
        var roles = Fresh(_roles);
        var access = _users.TryGetValue(userId, out var cachedUser) && Fresh(cachedUser) is not null ? cachedUser : null;
        if (roles is null || access is null)
        {
            if (roles is null)
            {
                roles = new(await store.LoadRolePermissionsAsync(ct), time.GetUtcNow());
                if (Interlocked.Read(ref _generation) == generation)
                {
                    _roles = roles;
                }
            }
            if (access is null)
            {
                access = new(await store.LoadUserAccessAsync(userId, ct), time.GetUtcNow());
                if (Interlocked.Read(ref _generation) == generation)
                {
                    _users[userId] = access;
                }
            }
        }

        return OperationActorFactory.FromPrincipal(principal, roles.Value, access.Value);
    }

    /// <summary>Drops every cached entry; the next check of any user reloads from the database.</summary>
    public void Invalidate()
    {
        Interlocked.Increment(ref _generation);
        _roles = null;
        _users.Clear();
    }

    private Cached<T>? Fresh<T>(Cached<T>? cached) =>
        cached is not null && time.GetUtcNow() - cached.LoadedAt < options.Value.RevalidationInterval ? cached : null;
}

/// <summary>Where <see cref="PermissionResolver"/> reads role permissions and a user's stored access.</summary>
public interface IPermissionStore
{
    Task<IReadOnlyDictionary<string, ImmutableHashSet<string>>> LoadRolePermissionsAsync(CancellationToken ct);

    /// <summary>The user's stored access, or <c>null</c> when the user has no row.</summary>
    Task<StoredUserAccess?> LoadUserAccessAsync(string userId, CancellationToken ct);
}

/// <summary>The database <see cref="IPermissionStore"/>: one short-lived context per load (<see cref="PermissionQueries"/>).</summary>
internal sealed class DatabasePermissionStore(IDbContextFactory<AppDbContext> contextFactory) : IPermissionStore
{
    public async Task<IReadOnlyDictionary<string, ImmutableHashSet<string>>> LoadRolePermissionsAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await PermissionQueries.LoadRolePermissionsAsync(db, ct);
    }

    public async Task<StoredUserAccess?> LoadUserAccessAsync(string userId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await PermissionQueries.LoadUserAccessAsync(db, userId, ct);
    }
}
