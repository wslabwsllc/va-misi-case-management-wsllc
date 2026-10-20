# 0007. Local Identity authentication, roles and the permission matrix

* Status: accepted
* Owner: E3

## Context

Spec 4.2 requires at least two user roles with different permissions; 4.1 forbids Government identity services and
credentials; 4.7 puts production identity and SSO out of scope; 4.5 serves the app over plain HTTP on
`localhost:8080`. The spec does not name the roles or say which role may do what (OQ-3), nor how users sign in
(OQ-18). The app is a Blazor Web App whose pages run as Interactive Server components (ADR for render mode: E5), so a
user's identity must be available both on HTTP requests (pages, the attachment download endpoint) and inside
long-lived SignalR circuits, where `HttpContext` is absent or stale.

## Decision

**Authentication.** Local ASP.NET Core Identity (`AddIdentityCore<AppUser>().AddRoles<AppRole>()
.AddEntityFrameworkStores<AppDbContext>().AddSignInManager()`) with the Identity application cookie
(`CaseIntake.Auth`, HttpOnly, `SameSite=Lax`, 8-hour sliding expiry, not `Secure` because the app is HTTP-only).
`AppUser`/`AppRole` live in Persistence so `AppDbContext` does not depend on the Identity feature project. Users sign
in with their email as user name on a static SSR page (`/account/login`); sign-out is `POST /account/logout`. Both
are plain HTTP form posts protected by antiforgery tokens. There is no self-registration, password reset or external
login (OQ-18). Data Protection keys persist on the `dataprotection` volume, so cookies survive container recreation.

**Roles and the adopted permission matrix (assumption recorded under OQ-3).** Four case roles that build on each
other, `Auditor` ⊂ `IntakeClerk` ⊂ `CaseWorker` ⊂ `Supervisor`, plus the separate `QualityAnalyst` added by the test
results module ([ADR 0012](0012-test-results-module.md)). Auditors review without changing anything; intake clerks
open cases at the front door; case workers do the day-to-day work; supervisors make the triage and closure decisions.
The spec requires at least two roles; the extra two separate read-only oversight and intake from casework.

| Permission | Auditor | IntakeClerk | CaseWorker | Supervisor | QualityAnalyst |
|---|:-:|:-:|:-:|:-:|:-:|
| `cases.view` | ✔ | ✔ | ✔ | ✔ | ✔ |
| `attachments.download` | ✔ | ✔ | ✔ | ✔ | – |
| `cases.create` / `veteran.lookup` / `attachments.upload` | – | ✔ | ✔ | ✔ | – |
| `cases.edit` | – | – | ✔ | ✔ | – |
| `cases.transition.Triaged.InProgress` | – | – | ✔ | ✔ | – |
| `cases.transition.InProgress.Resolved` | – | – | ✔ | ✔ | – |
| `cases.transition.New.Triaged` | – | – | – | ✔ | – |
| `cases.transition.Resolved.Closed` | – | – | – | ✔ | – |
| `cases.transition.Resolved.InProgress` (reopen) | – | – | – | ✔ | – |
| `testresults.view` / `testresults.manage` | – | – | – | – | ✔ |

The UI shows roles by display name (`AppRoles.DisplayName`: "Intake clerk", "Case worker", "Quality analyst").

A user with several roles gets the union; an unknown role grants nothing. The matrix is the single
`RolePermissionMap` in `CaseIntake.Contracts.Security`, shared by the authorization handler, both `ICurrentUser`
adapters and any module, without one feature referencing another. A golden `[Theory]` in
`CaseIntake.Identity.Tests` asserts it cell by cell.

**Authorization.** One policy per permission (policy name = permission name, the `Permissions.*` constants) for the
six fixed permissions and the five transitions. A `PermissionRequirement` handler derives permissions from the trusted
role claims Identity issues at sign-in, through the same map; claims transformation and permission claims are not
used. A fallback policy requires an authenticated user everywhere unless an endpoint opts out (`[AllowAnonymous]`:
login, access-denied, logout, health). Enforcement is layered:

- pages: `@attribute [Authorize(Policy = Permissions.X)]` (the router's `AuthorizeRouteView` handles interactive
  navigation; the authorization middleware handles HTTP requests);
- controls: `<AuthorizeView Policy="...">` (E5) only hides what the user cannot do;
- endpoints: `RequireAuthorization(Permissions.X)` plus `.DisableCookieRedirect()` so API-style endpoints answer
  401/403 instead of redirecting to the login page;
- services: every public service method checks `OperationActor.HasPermission` itself (defense in depth).

Every denial logs the shared `AuthorizationDenied` warning (EventId 3005) with user ID, permission and resource ID:
the authorization middleware for HTTP requests, the access-denied component for interactive navigation, and the
services for operations.

**Current user.** `ICurrentUser.GetAsync` returns an immutable `OperationActor` snapshot (Identity user ID, user name
as display name, roles, permissions). There are two explicit adapters, selected by the composition root:
`CircuitCurrentUser` (the unkeyed registration, for components and services in a circuit) reads
`AuthenticationStateProvider` on every call; `HttpCurrentUser` (keyed `CurrentUserSources.Http`, for minimal API
endpoints) reads the request principal. Circuit identity is never inferred from `HttpContext`, and no principal is
cached. `IdentityRevalidatingAuthenticationStateProvider` re-checks each circuit's security stamp every minute (and
the cookie validator checks HTTP requests at the same interval) in its own DI scope, so a changed password, role
membership or revoked stamp ends the circuit's access.

**Request security baseline.** Antiforgery on every cookie-authenticated form post (`UseAntiforgery`; Blazor forms
and the logout endpoint's form binding validate it). Interactive circuit events are not form posts: they are
authorized per operation through `ICurrentUser`, and the `_blazor` hub itself requires an authenticated user.
Lockout after five failed attempts for five minutes (`lockoutOnFailure: true`, lockout enabled on seeded users).
A small middleware adds `X-Content-Type-Options: nosniff`, `Referrer-Policy: same-origin`, `X-Frame-Options: DENY`,
`Cross-Origin-Opener-Policy: same-origin` and a CSP: `script-src 'self'`, `connect-src 'self'` plus the request
host's `ws:`/`wss:` origin for the SignalR circuit, `style-src 'self' 'unsafe-inline'`, `object-src 'none'`,
`frame-ancestors 'none'`, `form-action 'self'`. Login and logout redirect only to local return URLs.

**Demo users.** `IdentitySeedContributor` (order 10, before the case seed) creates every role and one
user per role, `auditor@`, `intake@`, `caseworker@`, `supervisor@` and `qa@example.test` (reserved `.test` TLD), with
stable IDs and the `Seed:DemoPassword` password. The sign-in page lists them whenever `Seed:DemoPassword` is configured
(docker compose and `dotnet run`), so a deployment without demo seeding reveals no account names. Re-seeding adds only what is missing and never resets a password.

## Alternatives considered

- **External IdP (Entra ID, Keycloak, OpenID Connect):** realistic for production, but out of scope (4.7), needs
  network or another container, and Government identity services are prohibited (4.1).
- **Identity's default UI / the full template account area:** brings registration, password reset, 2FA and email
  flows the spec does not need; trimmed to login and logout (OQ-18).
- **Role checks (`[Authorize(Roles = ...)]`) instead of permissions:** simpler, but scatters the matrix across pages
  and services; changing who may triage would touch many files. Permissions keep the matrix in one table.
- **Permission claims added by claims transformation:** transformation does not run for circuits, so circuit and HTTP
  checks could disagree; deriving permissions from roles at check time keeps one rule.
- **One `ICurrentUser` that picks `HttpContext` when present:** inside a circuit `HttpContext` can belong to the
  initial request (stale) or be null; an explicit adapter per entry point avoids silently crossing identities.
- **CSP with nonces:** stronger for inline scripts, but the app has none and Blazor Server needs no inline script;
  `'self'` is enough.

## Tradeoffs

- Plain HTTP means the cookie is not `Secure` and credentials cross the loopback interface unencrypted; acceptable
  only for the local evaluation the spec describes (limitations document).
- The matrix is compiled in; changing it is a code change plus the golden test, which is deliberate.
- `style-src 'unsafe-inline'` is allowed because Blazor's reconnect/error UI sets inline styles; scripts stay strict.
- Revalidation every minute costs one small query per open circuit per minute; access is revoked within that interval,
  not instantly.
- Sign-out clears the browser's cookie but does not revoke copies of it: cookie authentication is stateless, so a cookie
  captured before sign-out stays valid until it expires or the user's security stamp changes.

## Consequences

- New pages need only `[Authorize(Policy = Permissions.X)]`; anything else still requires sign-in via the fallback.
- The fallback policy also applies to endpoint-served static assets: if `MapStaticAssets()` replaces `UseStaticFiles()`,
  those endpoints must be marked `.AllowAnonymous()` so the login page can load its script and styles.
- E4's download endpoint must use `RequireAuthorization(Permissions.AttachmentsDownload).DisableCookieRedirect()` and
  resolve `[FromKeyedServices(CurrentUserSources.Http)] ICurrentUser`, passing it to a request-scoped service instance.
- Identity stores use the request-scoped `AppDbContext` registered by `AddDbContextFactory`; only static SSR account
  requests resolve them. Circuit services keep using `IDbContextFactory` and must not inject `UserManager`.
- Browser end-to-end tests (E5-S9) sign in with the seeded accounts; `TestAuthHandler` is only for isolated
  authorization tests.
