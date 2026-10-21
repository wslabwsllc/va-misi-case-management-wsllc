using System.Collections.Immutable;
using CaseIntake.Contracts.Security;
using CaseIntake.Identity.Authorization;
using CaseIntake.Persistence;
using Microsoft.Extensions.Options;

namespace CaseIntake.Identity.Tests;

/// <summary>
/// An <see cref="IPermissionStore"/> without a database: every built-in role starts with its <see cref="RolePermissionMap"/>
/// defaults, users have no stored row unless added. Counts loads so cache behavior can be asserted.
/// </summary>
internal sealed class InMemoryPermissionStore : IPermissionStore
{
    public static IReadOnlyDictionary<string, ImmutableHashSet<string>> DefaultMatrix { get; } =
        RolePermissionMap.Roles.ToDictionary(r => r, r => RolePermissionMap.PermissionsFor([r]), StringComparer.Ordinal);

    public Dictionary<string, ImmutableHashSet<string>> Roles { get; } = new(DefaultMatrix, StringComparer.Ordinal);
    public Dictionary<string, StoredUserAccess> Users { get; } = new(StringComparer.Ordinal);
    public int RoleLoads { get; private set; }
    public int UserLoads { get; private set; }

    public Task<IReadOnlyDictionary<string, ImmutableHashSet<string>>> LoadRolePermissionsAsync(CancellationToken ct)
    {
        RoleLoads++;
        return Task.FromResult<IReadOnlyDictionary<string, ImmutableHashSet<string>>>(new Dictionary<string, ImmutableHashSet<string>>(Roles));
    }

    public Task<StoredUserAccess?> LoadUserAccessAsync(string userId, CancellationToken ct)
    {
        UserLoads++;
        return Task.FromResult(Users.GetValueOrDefault(userId));
    }

    /// <summary>A resolver over the unchanged default matrix, for tests that only need role-based permissions.</summary>
    public static PermissionResolver DefaultResolver { get; } = new InMemoryPermissionStore().Resolver();

    public PermissionResolver Resolver(TimeProvider? time = null) =>
        new(this, time ?? TimeProvider.System, Options.Create(new IdentityModuleOptions()));

    public static StoredUserAccess Access(string userId, bool isActive = true, IReadOnlyDictionary<string, PermissionEffect>? overrides = null) =>
        new(userId, isActive, [], overrides ?? new Dictionary<string, PermissionEffect>());
}
