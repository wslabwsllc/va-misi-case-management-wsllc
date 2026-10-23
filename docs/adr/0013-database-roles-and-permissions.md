# 0013. Database-backed roles, permissions and user administration

* Status: accepted (supersedes the matrix, user-administration and current-user parts of [0007](0007-roles-and-permissions.md))
* Owner: E3

## Context

The requirement change (Change 1 in [change-implementation-record.md](../change-implementation-record.md)) asks for an
Administrator role, permissions stored in the database instead of compiled in, roles an administrator can configure,
per-user Allow and Deny overrides, and fuller user management: edit, activate and deactivate, a detail page, several
roles per user, the effective permissions, and paging. ADR 0007 compiled the matrix into `RolePermissionMap`, derived
permissions from role claims synchronously, and let supervisors only list users and add a user with one role.

Constraints carried over from ADR 0007: one rule for pages, endpoints and services; circuits never read `HttpContext`;
`UserManager` is never held by a circuit; denials log `AuthorizationDenied` (3005); the existing permission names, which
tests, pages and services use, keep working.

## Decision

**Data model** (migration `C1_DatabasePermissions`, Persistence):

| Table | Holds |
|---|---|
| `Permissions` | The catalog: name (key), group, label, description. The seed keeps it equal to `PermissionCatalog` in code; a permission removed from the code takes its grants with it (cascade). |
| `RolePermissions` | (role, permission) pairs. |
| `UserPermissionOverrides` | (user, permission, `Allow` or `Deny`, changed at, changed by). |
| `AspNetRoles` + `Description`, `IsBuiltIn`, `DefaultPermissionsApplied` | Built-in roles cannot be renamed or deleted; the list records which default permissions the seed has already granted the role. |
| `AspNetUsers` + `IsActive` (default true), `CreatedUtc`, `LastSignInUtc`, `DeactivatedUtc` | Users are deactivated, never deleted. |

**Permission names.** The existing names stay (`cases.view`, `cases.edit`, `users.manage`, ...); one is added,
`roles.manage`. Each still has one authorization policy of the same name. Where the change uses other words, the
mapping is: read = `cases.view`, update = `cases.edit`, create = `cases.create`; user administration = `users.manage`;
role administration = `roles.manage`.

**Effective permissions** (`EffectivePermissions`, Contracts): the union of the permissions of the user's roles, plus the
user's Allow overrides, minus the user's Deny overrides. Deny wins, with one exception: a user who holds `cases.create`
also holds `veteran.lookup`, even over a Deny, because a case cannot be created without a veteran (added 2026-10-21 with
the required lookup; assumptions A8 and C1-3). An inactive user holds nothing.

**Roles.** Six built-in roles: the five of ADR 0007 plus `Administrator`, which holds every permission. The seed grants
each default permission from `RolePermissionMap` to its built-in role once (recorded in `DefaultPermissionsApplied`), so
an administrator's changes, including removing every permission, survive re-seeding, while a default added later
reaches already-seeded databases ([ADR 0014](0014-system-audit-log.md)); Administrator is re-granted every catalog permission on every
run and its permissions cannot be edited. Until the seed has written a built-in role's defaults (a migrated but unseeded
database, as in integration tests), that role grants its compiled defaults. Administrators create, rename, describe and
delete custom roles; a role with members cannot be deleted. `RolePermissionMap` remains the seeded default and the
golden test, and `PermissionCatalog` describes every permission for the UI.

**Resolution.** `PermissionResolver` (Identity, singleton) turns a principal into the `OperationActor` snapshot for the
authorization handler and both `ICurrentUser` adapters: roles from the trusted role claims, permissions from the stored
role permissions and the user's stored active flag and overrides (`PermissionQueries`, Persistence). It caches the role
map and each user's access for at most the revalidation interval (one minute); every administration change clears the
cache, so this instance applies it at the next permission check. Modules that ask "who holds this permission" (case
assignees) use `PermissionQueries.ActiveUserIdsHoldingAsync` directly.

**Sessions.** Role membership is carried by the cookie's role claims, so a role change (and a rename of a custom role,
for its members) updates the security stamp and the user's sessions are refreshed by signing in again within the
revalidation interval. Deactivation removes every permission at once (resolver), ends sessions within the interval
(security stamp, and revalidation refuses inactive users) and blocks sign-in before the password is checked (no
lockout attempts accumulate). Overrides and role permission changes need no new sign-in.

**No escalation.** Every administration change follows `AdministrationRules`:

- nobody adds, removes, allows or denies a permission they do not hold, or adds or removes a role holding one;
- nobody manages a user whose permissions (as if active) include one the actor lacks; the page is then read-only and says why;
- nobody changes their own roles, status or overrides;
- nobody removes Manage roles from a role they hold if they would lose it;
- the Administrator role cannot be changed.

Because the actor of any change must hold every permission of the user they change and cannot change themselves, at
least one active user with `users.manage` and `roles.manage` always remains.

**User administration** (`users.manage`): a paged, filterable list (email search, role, status); add with one or more
roles; a detail page with account facts, email and roles, deactivate or reactivate behind a confirmation dialog,
unlock after failed sign-ins, and a permissions table showing for each permission the roles that grant it, the override
(Inherit / Allow / Deny radio group) and the result. Every change carries the user's Identity concurrency stamp; a
stale stamp is a Conflict and the latest version is shown. Recording the last sign-in writes that column directly, so a
sign-in does not change the stamp. Each change runs as one transaction inside the provider's retrying execution
strategy with a fresh DI scope per attempt (`DatabaseUnit`). Supervisors keep `users.manage`, limited by the rules
above (they cannot grant Quality analyst or Administrator).

**Role administration** (`roles.manage`, Administrator by default): a list with members and permissions, and an editor
with the permissions as checkboxes grouped by catalog group; permissions the actor lacks are shown disabled with the
reason.

**Log events** (Identity, 30xx): `UserUpdated` 3014, `UserStatusChanged` 3015, `UserPermissionOverridesChanged` 3016,
`UserUnlocked` 3017, `RoleCreated` 3020, `RoleUpdated` 3021, `RoleDeleted` 3022; `UserCreated` 3011 now lists roles;
`IdentitySeeded` 3010 also counts grants; `LoginFailed` 3002 has the reason `Inactive`. IDs, role and permission names
only. A searchable change history in the app is part of the system-wide audit log (next change).

## Alternatives considered

- **Roles from the database on every check instead of from claims:** role changes would apply without signing in again,
  but test principals and the access-denied page would need a stored user, and the cookie would no longer describe the
  session. Claims plus a stamp change keep ADR 0007's model; permissions, overrides and status are live.
- **Permission claims in the cookie:** stale until the next sign-in, and transformation does not run for circuits (ADR 0007).
- **Rename the permissions to `cases.read` / `cases.update`:** breaks every page attribute, service check, test and the
  golden matrix for no behavior change; the mapping above is documented instead.
- **Delete users:** user IDs are referenced by case history and attachments; deactivation keeps them.
- **No cache:** one or two small queries per permission check, and the navigation alone evaluates about a dozen
  policies per render. A one-minute cache cleared on every change keeps changes immediate in this single-instance app.

## Tradeoffs

- The cache is per process. Another instance (none in this deployment) would see a change within one minute.
- A cache miss makes the authorization check asynchronous, so `AuthorizeRouteView` can briefly show "Checking your
  access"; `FocusOnNavigation` therefore waits up to three seconds for the new page's h1 before moving focus.
- A role change signs the member in again within a minute, rather than applying in place.
- Failed sign-ins and lockouts still change the concurrency stamp (Identity writes those counters); an administrator
  editing that user at the same moment sees a Conflict and the latest version.

## Consequences

- New permissions are added to `Permissions` (constant), `RolePermissionMap` (default grants and `AllPermissions`) and
  `PermissionCatalog` (description); the golden test and the catalog test must change with them. The seed adds the
  permission to the catalog, to Administrator, and once to each built-in role whose defaults include it.
- Pages keep `[Authorize(Policy = Permissions.X)]`; services keep `OperationActor.HasPermission`.
- Integration tests that sign in with `TestAuthHandler` keep working on unseeded databases through the built-in defaults.
- `IUserAdministrationService` and `IRoleAdministrationService` (Contracts) are the only way the UI changes users and
  roles; both run in the circuit and resolve Identity managers in a fresh scope per call.
