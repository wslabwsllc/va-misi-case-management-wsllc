# Case intake and triage implementation plan

**Source:** Technical Build Specification, Sections 4.1 to 4.7 (Case Intake and Triage coding challenge)
**Review status:** Reviewed against specification sections 4.1–4.7. All specified deliverables have story ownership and verification criteria. Begin with G0; implementation and runtime verification remain to be performed.
**Target stack:** .NET 10 (LTS) · C# 14 · ASP.NET Core Blazor Web App · EF Core 10 · PostgreSQL · Docker Compose v2 (Linux x86-64)
**Execution model:** Engineers use AI agents across five epic ownership areas. Complete foundation gate G0 before parallel feature work. Integrate a real working slice at G1. Gates express dependencies, not dates or effort commitments.

---

<details>
<summary><span style="color: cyan"><b>0. Introduction</b></span></summary>

### 0.1 Assumptions

The specification leaves these details to the offeror. Adopt the decisions below for implementation and record their rationale in ADRs. Section 8 tracks these decisions separately from the unknown Government requirement change. No business clarification blocks foundation work.

| # | Assumption | Rationale |
|---|---|---|
| A1 | **.NET 10 LTS** (C# 14, ASP.NET Core 10, Blazor 10, EF Core 10). Exact patch versions get pinned during G0 in `global.json` and `Directory.Packages.props`, then recorded in `docs/versions.md`. | Spec 4.5 asks for a currently supported LTS and for the exact versions to be named. .NET 10 is the current LTS. |
| A2 | **Render mode:** Interactive Server for the app pages. The Identity account pages (login/logout) stay static SSR. | No public API is needed, services are called directly, and it's the simplest way to handle auth and file streaming. The tradeoffs go in an ADR (spec 4.5). |
| A3 | **Database:** PostgreSQL in a container, accessed through EF Core 10 with the Npgsql provider. | EF Core is the baseline the spec prefers. Postgres runs on Linux x86-64 and is lightweight. SQL Server is the alternative (ADR). |
| A4 | **Authentication:** local ASP.NET Core Identity with seeded synthetic users. No external IdP. | Production identity is out of scope (4.7), and Government identity services must not be used (4.1). |
| A5 | **Two roles** (adopted names): `CaseWorker` and `Supervisor`, with the adopted permission matrix in E3-S2. | The spec needs at least two roles with different permissions but doesn't name them. |
| A6 | **"Audit information"** means audit fields on each case (Created/Updated At/By, in UTC) plus an append-only audit trail of changes. | It's ambiguous, so both readings are covered (OQ-2). |
| A7 | **Initial status** of a new case is `New`. Every transition **not** listed in 4.3 is rejected. | "Controlled status-workflow transitions" plus "the following transitions are allowed" (OQ-10, OQ-11). |
| A8 | The **Veteran Lookup** happens during intake (optional). A snapshot of the result (name, address) is saved on the case. The full SSN is **not** persisted (last 4 only). | The spec doesn't say what the lookup is for (OQ-5). |
| A9 | **Attachments** go on a Docker named volume. Their metadata goes in the DB. The size limit and type allowlist are configurable (adopted defaults: 10 MiB (10,485,760 bytes), common document and image types). | No constraints are given (OQ-6). Malware scanning is out of scope (4.7). |
| A10 | **Accessibility target:** WCAG 2.2 AA used as the engineering yardstick for the primary workflow. | The spec names behaviors, not a standard (OQ-7). |
| A11 | **"Primary workflow"** = sign in → case list → intake (with lookup) → detail → edit → status transition → attachment upload/download. | Not defined (OQ-8). |
| A12 | The application tier is in-process (a modular monolith inside the Blazor host). The only separate service is the Veteran Lookup mock container. | Keeps coupling and ops minimal. The spec doesn't ask for a public REST API. |

### 0.2 Solution structure

```
/ (repo root)
├── docker-compose.yml                 # E1  (services: db, migrator, web, veteran-lookup, tests)
├── global.json                        # E1  (SDK pin; standard dotnet test with VSTest)
├── Directory.Build.props              # E1  (net10.0, Nullable, TreatWarningsAsErrors, LangVersion 14)
├── Directory.Packages.props           # E1  (Central Package Management, all versions pinned)
├── BannedSymbols.txt                  # E1  (bans DateTime.Now / DateTimeOffset.Now, etc.)
├── CODEOWNERS                         # E1  (folder ownership per epic)
├── docs/
│   ├── adr/                           # E1 template; every epic contributes ADRs
│   ├── contracts/veteran-lookup.openapi.yaml   # faithful OpenAPI transcription of spec 4.6 (E4)
│   ├── versions.md, assumptions-and-limitations.md, traceability.md
├── src/
│   ├── CaseIntake.Contracts/          # SHARED (foundation, all devs; E1 is gatekeeper)
│   ├── CaseIntake.Domain/             # Cases/ (E2), Attachments/ (E4) entities
│   ├── CaseIntake.Persistence/        # E1: AppDbContext, Migrations, seeding orchestration
│   │   └── Configurations/{Cases,Identity,Attachments}/  # owned by E2/E3/E4
│   ├── CaseIntake.ServiceDefaults/    # E1: logging, health checks, optional telemetry
│   ├── CaseIntake.Cases/              # E2: case services, audit orchestration, query
│   ├── CaseIntake.Identity/           # E3: Identity, roles, actor adapters
│   ├── CaseIntake.Attachments/        # E4: attachment service, store, download endpoint
│   ├── CaseIntake.VeteranLookup.Client/  # E4: typed HttpClient
│   ├── CaseIntake.VeteranLookup.Mock/    # E4: standalone mock service (own container)
│   ├── CaseIntake.MigrationService/   # E1: one-shot migrate + seed worker
│   └── CaseIntake.Web/                # E5: Blazor UI (Program.cs composition: E1; Components/Account: E3)
└── tests/
    ├── CaseIntake.TestKit/            # E1: fixtures, builders, fakes of every contract
    ├── CaseIntake.Cases.Tests/        # E2 (unit)
    ├── CaseIntake.Identity.Tests/     # E3 (unit)
    ├── CaseIntake.Attachments.Tests/  # E4 (unit)
    ├── CaseIntake.VeteranLookup.Tests/   # E4 (client + mock + OpenAPI conformance)
    ├── CaseIntake.Web.Tests/          # E5 (bUnit component tests)
    ├── CaseIntake.IntegrationTests/   # E1 harness; all epics add tests (WebApplicationFactory + Postgres)
    └── CaseIntake.E2E/                # E5 (Playwright for .NET + axe-core accessibility)
```

**Project dependency rule (enforced by an architecture test in E1-S7):**
- `Contracts` and `Domain` have no EF Core, Identity, or feature-project dependencies. Domain owns `CaseStatus` and workflow rules; Contracts may reference Domain for these types.
- `Persistence` references Domain and Contracts and owns `AppDbContext`, `AppUser`, `AppRole`, EF configurations, and migrations.
- `Cases`, `Identity`, and `Attachments` reference Persistence, Domain, and Contracts. `VeteranLookup.Client` references Contracts only.
- `Web` and `MigrationService` are composition roots and may reference the modules they register. The standalone mock does not reference application persistence.
Identity user/role types belong to Persistence, not the Identity feature project. This prevents the Persistence → Identity → Persistence cycle. Configuration files remain owned by the named epic but compile into Persistence.
Feature modules **must not reference each other**. Public service contracts live in `CaseIntake.Contracts`; the shared audit helper lives in Persistence and never saves independently.
Each module exposes exactly one `services.AddXxxModule(configuration)` method, plus `app.MapXxxEndpoints()` where it has endpoints. That way `Program.cs` merges stay one-line changes.

### 0.3 Shared contracts and foundation agreement

The five epic owners merge a single foundation PR containing `CaseIntake.Contracts` and the entity shells in `CaseIntake.Domain`. After merge, any change to a contract needs a PR approved by the consuming epic owners. Changes are **additive only** unless everyone agrees.

```csharp
namespace CaseIntake.Contracts.Cases;

using CaseIntake.Domain.Cases;

public sealed record CreateCaseRequest(string Title, string? Description, VeteranSnapshot? Veteran);
public sealed record UpdateCaseRequest(string Title, string? Description, uint Version);   // Version = optimistic concurrency token
public sealed record ChangeStatusRequest(CaseStatus TargetStatus, uint Version);
public sealed record VeteranSnapshot(string Name, string Address, string SsnLast4);

public sealed record CaseSummaryDto(long Id, string Title, CaseStatus Status,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record CaseDetailDto(long Id, string Title, string? Description, CaseStatus Status,
    DateTimeOffset CreatedAtUtc, string CreatedBy, DateTimeOffset UpdatedAtUtc, string UpdatedBy,
    uint Version, VeteranSnapshot? Veteran, IReadOnlyList<CaseStatus> AllowedNextStatuses);
public sealed record AuditEntryDto(long Id, long CaseId, DateTimeOffset OccurredAtUtc, string Actor,
    string Action, CaseStatus? FromStatus, CaseStatus? ToStatus, string? Details);

public enum CaseSortField { Id, Title, Status, CreatedAt, UpdatedAt }
public enum SortDirection { Ascending, Descending }
public sealed record CaseListQuery(int Page = 1, int PageSize = 25,
    IReadOnlyCollection<CaseStatus>? Statuses = null, string? Search = null,
    CaseSortField SortBy = CaseSortField.UpdatedAt, SortDirection Direction = SortDirection.Descending);
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public interface ICaseService
{
    Task<Result<CaseDetailDto>> CreateAsync(CreateCaseRequest request, CancellationToken ct);
    Task<Result<CaseDetailDto>> GetAsync(long id, CancellationToken ct);
    Task<Result<CaseDetailDto>> UpdateAsync(long id, UpdateCaseRequest request, CancellationToken ct);
    Task<Result<CaseDetailDto>> ChangeStatusAsync(long id, ChangeStatusRequest request, CancellationToken ct);
    Task<Result<PagedResult<CaseSummaryDto>>> ListAsync(CaseListQuery query, CancellationToken ct);
    Task<Result<IReadOnlyList<AuditEntryDto>>> GetAuditTrailAsync(long id, CancellationToken ct);
}
```

```csharp
namespace CaseIntake.Contracts.Attachments;
public sealed record AttachmentDto(Guid Id, long CaseId, string FileName, string ContentType,
    long SizeBytes, DateTimeOffset UploadedAtUtc, string UploadedBy);
public sealed record AttachmentContent(Stream Content, string FileName, string ContentType, long SizeBytes);
public interface IAttachmentService
{
    Task<Result<AttachmentDto>> UploadAsync(long caseId, string fileName, string contentType,
        long declaredSize, Stream content, CancellationToken ct);
    Task<Result<IReadOnlyList<AttachmentDto>>> ListAsync(long caseId, CancellationToken ct);
    Task<Result<AttachmentContent>> OpenReadAsync(long caseId, Guid attachmentId, CancellationToken ct);
}
public static class AttachmentRoutes      // download URL used by the UI (E5) and implemented by E4
{
    public static string Content(long caseId, Guid id) => $"/cases/{caseId}/attachments/{id}/content";
}
```

```csharp
namespace CaseIntake.Contracts.VeteranLookup;
public enum VeteranLookupOutcome { Found, NotFound, InvalidInput, Unavailable, Forbidden }
public sealed record VeteranLookupResult(VeteranLookupOutcome Outcome, string? Name, string? Address);
public interface IVeteranLookupClient { Task<VeteranLookupResult> LookupAsync(string ssn, CancellationToken ct); }
```

```csharp
namespace CaseIntake.Contracts.Security;
public static class AppRoles { public const string CaseWorker = "CaseWorker"; public const string Supervisor = "Supervisor"; }
public static class Permissions
{
    public const string CasesView = "cases.view", CasesCreate = "cases.create", CasesEdit = "cases.edit";
    public const string AttachmentsUpload = "attachments.upload", AttachmentsDownload = "attachments.download";
    public const string VeteranLookup = "veteran.lookup";
    public static string Transition(CaseStatus from, CaseStatus to) => $"cases.transition.{from}.{to}";
}
public sealed record OperationActor(string? UserId, string? DisplayName,
    System.Collections.Immutable.ImmutableHashSet<string> Roles,
    System.Collections.Immutable.ImmutableHashSet<string> Permissions)
{
    public bool IsAuthenticated => UserId is not null;
    public bool HasPermission(string permission) => IsAuthenticated && Permissions.Contains(permission);
}
public interface ICurrentUser
{
    Task<OperationActor> GetAsync(CancellationToken ct);
}
```

```csharp
namespace CaseIntake.Contracts.Common;
public enum ErrorKind { Validation, NotFound, Forbidden, Conflict, InvalidTransition, ExternalUnavailable }
public sealed record Error(ErrorKind Kind, string Code, string Message,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null);
public sealed class Result<T>
{
    public T? Value { get; }
    public Error? Error { get; }
    public bool IsSuccess => Error is null;
    private Result(T? value, Error? error) { Value = value; Error = error; }
    public static Result<T> Success(T value) => new(
        value is null ? throw new ArgumentNullException(nameof(value)) : value, null);
    public static Result<T> Failure(Error error) => new(default,
        error ?? throw new ArgumentNullException(nameof(error)));
}
// ISeedContributor is an infrastructure contract in Persistence, not this project.
// Its execution receives an explicit operation scope; Identity seeding uses UserManager/RoleManager.
public static class Telemetry { public const string ActivitySourceName = "CaseIntake"; public const string MeterName = "CaseIntake"; }
// EventId ranges: E1 1000–1999, E2 2000–2999, E3 3000–3999, E4 4000–4999, E5 5000–5999
```

**Snippet imports:** These namespace blocks describe separate source files. Include explicit imports for `CaseIntake.Contracts.Common`, `CaseIntake.Domain.Cases`, and framework collection/IO/task types wherever used. Do not copy the blocks into one file. Case and attachment operations return `Result<T>.Success` or `Result<T>.Failure`; lookup returns `VeteranLookupResult` with its documented outcomes. Failure results never carry a payload. A success payload must be non-null for these contracts. Catch unexpected failures at the UI/HTTP boundary, log only safe diagnostic fields, and show a recoverable error instead of terminating a circuit. Caller cancellation remains cancellation. Never show or log raw provider/HTTP exception messages that can embed submitted values. Disable EF sensitive-data logging and HTTP body logging.

**Domain types:** `CaseIntake.Domain.Cases` owns `CaseStatus`, `ICaseWorkflow`, and `CaseWorkflow`. Define the five statuses once with values New=0, Triaged=1, InProgress=2, Resolved=3, Closed=4. `ICaseWorkflow` exposes `IsAllowed(CaseStatus from, CaseStatus to)` and `NextStatuses(CaseStatus from)`. Domain methods use this interface without referencing Contracts. Define `AuditEvent` in Domain with primitive actor identifiers on stored entities; Domain never references `OperationActor` from Contracts. Domain also owns `CaseFieldLimits` and pure `CaseTextRules`: normalize Title, validate text, and return field-name/message pairs. Contracts maps those pairs into request errors. Domain methods, services and UI invoke the same rules.

**Audit contract:** Persistence owns `AuditAppender.Append(AppDbContext db, Case caseEntity, OperationActor actor, AuditEvent auditEvent)`. Domain owns `AuditEvent`, containing action, UTC occurrence time, optional from/to status and safe details. The helper adds a `CaseAuditEntry` using the case navigation, returns without saving, and never opens a context or transaction. E2 and E4 call this shared helper with their operation's context. The helper is shared persistence infrastructure, not a public feature-service interface.

**Operation contract:** Each public service method awaits `ICurrentUser.GetAsync(ct)` once and checks the returned actor’s permissions. Case/attachment methods create their own context through `IDbContextFactory<AppDbContext>` and dispose it before returning; lookup needs no database context. No actor is accepted from browser input. Pass the immutable actor to persistence audit helpers; pass primitive actor identifiers to Domain when needed. Download streams transfer disposal responsibility to the HTTP result and do not keep a context open.

**Seed contract:** Persistence owns `ISeedContributor` with `int Order { get; }` and `Task SeedAsync(CancellationToken ct)`. The migrator resolves each contributor in its own DI scope. Contributors resolve their own factory or Identity stores, own their transaction, check stable seed keys, and fail startup if any Identity result or persistence operation fails. No EF `DbContext` parameter leaks into Contracts.

**Other foundation agreements:**
- **Time:** inject `TimeProvider`. Never use `DateTime.Now` or `DateTimeOffset.Now` (BannedApiAnalyzers enforces this). Every stored timestamp is a UTC `DateTimeOffset` mapped to `timestamptz`.
- **Entities:** the `Case`, `CaseAuditEntry` and `Attachment` shells land in `CaseIntake.Domain` in the contract PR. Each owning epic writes its own `IEntityTypeConfiguration<T>`. `AppDbContext` (E1) applies them all with `ApplyConfigurationsFromAssembly`.
- **Migrations:** one migration per PR, named `YYYYMMDDHHmm_<Epic>_<Change>`. When the snapshot conflicts, rebase and regenerate. E1 reviews every migration.
- **Fakes:** `CaseIntake.TestKit` ships in-memory fakes for every contract, so E5 can build the UI before E2–E4 merge (`AddFakeModules()`, Development and tests only, removed before DoD).
- **Configuration keys:** `ConnectionStrings:CaseIntake`, `VeteranLookup:BaseUrl`, `Attachments:RootPath`, `Attachments:MaxBytes`, `Attachments:AllowedContentTypes`, `Attachments:AllowedExtensions`, `Seed:DemoPassword`. All of them have working defaults in `docker-compose.yml` so no manual configuration is needed.
- **PII:** an SSN never appears in a log, trace or exception message (enforced through safe-field logging and captured-log assertions; redaction is an additional safeguard).

### 0.4 Integration order

Milestones are dependency checkpoints. No calendar or staffing estimates apply.

| Milestone | Content | Exit gate |
|---|---|---|
| M0 foundation | E1-S1/S3/S7, E2-S1/S4, E3-S3: project graph, shared types, operation contexts/actors, audit helper, minimal test container | G0 |
| M1 working slice | Real Identity login, case creation/list/detail, persisted audit/UTC, browser smoke test; E1/E2/E3/E5 integrate partial stories | G1 |
| M2 complete behavior | Workflow, edit conflicts, lookup and attachments; UI uses real modules as each is delivered | G2 |
| M3 demonstration readiness | Complete suite, accessibility review, documentation, clean-start/offline/restart checks | G3 |
| M4 requirement change | Analyze and implement the Government change when supplied, then rerun original acceptance checks | G4 |

### 0.5 Definition of Done (applies to every story)

1. Every functional acceptance criterion has suitable automated coverage under `docker compose run --rm tests`, which exits 0. Documentation, manual keyboard checks, and clean-machine/offline verification retain explicit review evidence; automated tests do not substitute for those checks.
2. `docker compose up --build` on a clean clone starts, migrates and seeds everything, serving http://localhost:8080 with no manual steps.
3. The behavior is real and working: no static screens standing in for required behavior, hard-coded demo paths or placeholder implementations (spec 4.1). `AddFakeModules()` isn't registered in the composed app.
4. Only synthetic data is used. No external commercial or Government service is called at runtime.
5. Every timestamp it stores is in UTC. Structured logs are emitted for key operations, and they contain no SSN or secrets.
6. Every UI change can be operated by keyboard, has labels, announces validation, status and errors, and passes the axe checks (E5-S9).
7. Builds with zero warnings (`TreatWarningsAsErrors`). Code is reviewed by one other epic owner, and CODEOWNERS is respected.
8. If the story involved a design decision, its ADR or doc is updated, and the traceability table is updated.

### 0.6 Dependency and release gates

Preserve required behavior and defer optional infrastructure. E3 pairs on identity/service integration and browser coverage; E4 helps E1 container/test reproducibility. Run the browser smoke test at G1 and extend it with each capability.

| Gate | Required evidence | Accountable owner |
|---|---|---|
| G0 before parallel feature work | Acyclic dependency graph, operation-scoped context/actor design, explicit audit transaction contract, selected VSTest runner discovers and passes a test; deliberate failure returns nonzero; contexts and actor adapters work in HTTP and circuit paths | E1 with E2/E3/E4 |
| G1 first working slice | Fresh Compose startup; real login → create → list → detail; real DB, audit and UTC; same path in a browser test | E1/E2/E3/E5 |
| G2 complete capability scope | Five workflow edges, role denial, edit conflict, real HTTP lookup, durable upload/download, paging/filtering/sorting | Feature owners |
| G3 live demonstration readiness | Required commands pass from clean state, offline runtime check, repeated-start persistence check, accessibility review, exact versions and decisions documented, no runtime fakes | E1 with all owners |
| G4 final submission | Government change captured, implemented and tested; original requirements retained; final repository revision recorded and G3 rerun | E1 with affected owners |

Record command exit codes, test counts, tested repository revision and manual results in `docs/verification.md`. A failing required capability blocks G3/G4. Until the Government change is received, G4 remains pending; the initial demonstration can still satisfy G3.

### 0.7 Scope priorities

The traceability table covers every explicit requirement in sections 4.1–4.7. The main risks are implementation consistency and delivery capacity, rather than missing headline capabilities.

Deliver the required capabilities first. Structured operational logs satisfy the stated telemetry requirement. Distributed traces, metrics, an Aspire dashboard, broad component libraries, architecture-test packages, synthetic-data generators, search-performance benchmarks, and a separate project for each module are optional. Keep the five epic ownership boundaries even if several modules are folders in fewer projects. Optional work must not block required deliverables.

The role matrix, initial New status, rejection of unlisted transitions, optional intake lookup, attachment limits, title trimming, and Closed-case edit restriction are offeror decisions. Record them once in the assumptions document and apply them consistently. They do not require evaluator clarification before implementation. The Government-provided change is unknown and cannot be estimated or declared complete in advance.
---

### 0.8 Agent execution rules

For each assignment, use the story's acceptance criteria, Technical notes, Testing and Traces to as one contract. The owning epic supplies the code, migrations/configuration where applicable, tests, and documentation updates. Do not complete a story using a runtime fake or by weakening an acceptance criterion. Tests may use fakes for isolated units. Keep changes inside the assigned area; coordinate Contracts, Persistence, package pins, Compose and Program.cs through E1. Preserve existing work from other agents.

Foundation gate G0 consists of small portions of the named stories, not completion of those entire stories. Their final acceptance remains at the later gates. This avoids a dependency cycle between the test harness and features it will eventually test. After G0, follow this assignment order:

| Stories | Required inputs before implementation or integration |
|---|---|
| E1-S1 | Source specification and decisions in sections 0.1–0.3 |
| E1-S2/S3/S7 foundation slices | E1-S1 shared types and project shells; E2/E3/E4 contribute only shells and actor/audit wiring needed for G0 |
| E2-S1/S2, E3-S2/S3, E4-S1/S2, E5-S1/S2 | G0 contracts; these can proceed independently after foundation wiring |
| E1-S3 completion | E2/E3/E4 mappings in Persistence; generated migration reflects complete model |
| E1-S4, E3-S4, E2-S9 | Migration path plus E3 roles/stores; seed roles/users before cases and reference actual seeded actor IDs |
| E3-S1/S5/S6 | E3-S2/S3, E1 persistence/keys, seeded accounts; coordinate E5 account styling |
| E2-S3/S4/S5/S6 | E2-S1/S2, operation actor, context factory and audit helper |
| E2-S7/S8 | Case retrieval/mutation foundation, version mapping, role permissions and workflow rules |
| E4-S3 | Local mock contract and shared permission/actor contract |
| E4-S4/S5/S6 | Case schema, audit helper, context factory, actor adapters, configured writable volume |
| E5-S3/S4/S5/S6/S7/S8 | Corresponding real services and Identity for integrated completion; isolated component tests can precede integration |
| E1-S5/S6/S8 | Add UTC checks, logging and decisions as each feature lands; complete before G3 |
| E1-S7 completion and E5-S9 | All feature test projects plus real app/local service; extend G1 browser test incrementally |
| E1-S9 | Government change supplied; original G3 behavior retained |

Before marking a story complete, record changed artifacts, acceptance criteria exercised, exact verification command/results, and any limitation. A test not run is not a passed test. No browser/app runtime evidence exists at planning time.

</details>

---

<details>
<summary><span style="color: cyan"><b>EPIC 1 — Platform, Persistence & Delivery Pipeline (Dev 1)</b></span></summary>

**Goal:** Make `docker compose up --build` and `docker compose run --rm tests` work from the repo root from the first integrated slice. Provide the database, migrations, seeding, UTC time, telemetry, test harness and decision-documentation scaffolding that the other epics build on.

**Scope:** solution skeleton, version pinning, Dockerfiles, Compose topology, `AppDbContext`, migration/seed service, `TimeProvider` and UTC enforcement, `ServiceDefaults` (logging, OpenTelemetry, health), `TestKit` and the `tests` service, ADR framework and README, handling the requirement change.
**Out of scope:** business rules (E2), auth (E3), integrations (E4), UI (E5).

**Dependencies / interfaces:**
- *Provides:* `AppDbContext`, `ISeedContributor` execution, `TimeProvider`, `AddServiceDefaults()` / `MapDefaultEndpoints()`, `TestKit` fixtures (`PostgresDatabaseFixture`, `CaseIntakeWebFactory`, `TestAuthHandler`, `FakeTimeProvider`), compose services.
- *Consumes:* entity configurations from E2/E3/E4. E4's Dockerfile for `veteran-lookup`. Each module's `AddXxxModule()`.

### E1-S1 — Solution skeleton, version pinning and contracts project
**Story:** As a developer on any epic, I want a buildable solution with pinned SDK and package versions and the agreed contracts project, so that all epic owners can build on a stable base after G0.
**Acceptance criteria:**
- Given a clean clone, when I run `dotnet build`, then every project targets `net10.0` and builds with zero warnings.
- Given `global.json` and `Directory.Packages.props`, when I inspect them, then the .NET SDK, EF Core package versions and container runtime/shared-framework versions are pinned exactly, and `docs/versions.md` lists those exact versions (spec 4.5).
- Given a module project, when it references another feature module, then the architecture test fails.
- Given the foundation PR, when the solution builds, then Domain has no Contracts reference, Persistence has no Identity feature reference, and shared types have one definition.
**Technical notes:** `Directory.Build.props` (`TargetFramework net10.0`, `LangVersion 14`, `Nullable enable`, `TreatWarningsAsErrors`, `AnalysisLevel latest`). Central Package Management. `Microsoft.CodeAnalysis.BannedApiAnalyzers` with `BannedSymbols.txt`. `.editorconfig`. `CODEOWNERS` mapping folders to epics. Create the Web project from the `dotnet new blazor --interactivity Server --auth Individual` template, then restructure.
**Testing:** an architecture test (`NetArchTest.Rules` or reflection) in `CaseIntake.IntegrationTests/Architecture` asserting the dependency rule in 0.2.
**Traces to:** R-4.5-1, R-4.5-2, R-4.5-4

Before implementing package APIs, verify the supported .NET LTS, exact SDK/runtime/shared-framework versions, EF Core/Npgsql compatibility, and test-runner options against official documentation. Pin SDK and packages; record resolved runtime and shared-framework versions from the built image. Package pins alone do not pin Blazor's shared framework. Pin image versions or digests as appropriate and document their update process. The Dockerfile tag, Ubuntu variant, healthcheck utilities and non-root volume permissions must be proven by a build, not assumed.

### E1-S2 — Container images and Docker Compose topology
**Story:** As an evaluator, I want to run `docker compose up --build` from the repo root and get the whole application at http://localhost:8080, so that I can evaluate it without any manual setup.
**Acceptance criteria:**
- Given a machine with Docker Compose v2 and no other setup, when I run `docker compose up --build`, then images build, `db` becomes healthy, `migrator` finishes successfully, `veteran-lookup` and `web` become healthy, and http://localhost:8080 serves the login page.
- Given the compose file, when I inspect it, then every service runs as a Linux x86-64 container (`platform: linux/amd64`) and no runtime service points at an external host.
- Given the stack is running with outbound internet blocked, when I use every feature, then everything works (no runtime dependency on external services).
- Given the compose file, when I run `docker compose run --rm tests`, then a service named `tests` exists and runs (its content comes from E1-S7).
**Technical notes:** A shared evaluation image contains the published app, migrator, SDK, compiled tests, and matching Playwright browsers/OS dependencies. Both `web` and profiled `tests` use the same image name and build target. Therefore the required startup command builds test assets even though it does not run tests. Source browsers/dependencies from a pinned official Playwright container image and the .NET 10 SDK from a compatible pinned Microsoft SDK image; obtain packages from registries during build. Do not run browser-install scripts against download CDNs. Verify runtime/library compatibility at G0. Run web, migrator and tests as a non-root user with writable storage/results paths. Explicitly set `ASPNETCORE_URLS=http://+:8080` and map `8080:8080`. Services: `db` (`postgres:17`, `pg_isready` healthcheck, named volume), `migrator` (`depends_on: db: service_healthy`), `web` (`depends_on: migrator: service_completed_successfully`, `veteran-lookup: service_healthy`), `veteran-lookup` (E4 Dockerfile), `tests`. Named volumes `pgdata`, `attachments`, and `dataprotection`. All config through `environment:` with defaults. Disable CLI telemetry and first-run/workload notifications in evaluation containers. Health probes use tools included in the image and must not install curl or other utilities at runtime. No `.env` needed. Synthetic DB credentials only.
**Testing:** a smoke script (`scripts/verify-clean-start.sh`, run at G1/G3/G4) that brings the stack up and curls `/health/ready`. The tests service checks web health in E2E (E5-S9).
**Traces to:** R-4.1-1, R-4.1-2, R-4.5-5, R-4.5-6, R-4.5-7, R-4.5-9

Keep local CSS, fonts, JavaScript, and browser assets in the submitted images. Disable external telemetry exporters by default. Verify startup, the full primary workflow, and the complete test command with external egress unavailable after the initial build. Persist Data Protection keys in the `dataprotection` named volume, and test cookie validity after web-container recreation. Exempt login, static assets and intentionally public liveness/readiness endpoints from fallback authorization as needed. Readiness must exercise useful local dependencies without making its expected unauthenticated probe redirect to login.

### E1-S3 — AppDbContext, migrations and migration service
**Story:** As an operator, I want the database schema created and upgraded automatically at startup, so that the app is usable right after `docker compose up --build`.
**Acceptance criteria:**
- Given an empty database, when the `migrator` container runs, then all EF Core migrations are applied and the container exits with code 0.
- Given an already migrated database, when `migrator` runs again, then it makes no changes and exits 0 (idempotent).
- Given the DB is not ready yet, when `migrator` starts, then it retries with backoff for up to 60 s before failing with a clear log message.
- Given `web` starts, when the migrator failed, then `web` doesn't start (compose dependency).
- Given simultaneous business operations in one circuit, when they access data, then they receive distinct disposable contexts. Forced save failure commits neither a case mutation nor its audit entry.
**Technical notes:** `CaseIntake.Persistence.AppDbContext : IdentityDbContext<AppUser, AppRole, string>` (Identity types are owned by Persistence and implemented by E3, with shells agreed in foundation). `UseNpgsql(...)`. For retried explicit transactions, execute the complete database unit through the provider execution strategy; do not blindly replay filesystem writes. Use stable operation keys for upload retries. `ApplyConfigurationsFromAssembly`. `CaseIntake.MigrationService` is a `Host.CreateApplicationBuilder` worker that applies `MigrateAsync`, executes seed contributors in fresh scopes, and exits nonzero on failure. Design-time `IDesignTimeDbContextFactory`. Use `dotnet ef migrations add` (tool pinned in `dotnet-tools.json`).
**Testing:** integration test: apply migrations to a fresh test DB, then assert `GetPendingMigrationsAsync()` is empty and the model has no pending changes (`HasPendingModelChanges()`, EF Core 8+).
**Traces to:** R-4.5-4, R-4.5-7

Interactive Server DI scopes can last for a circuit. Never retain an `AppDbContext` for that lifetime. Use `IDbContextFactory<AppDbContext>` and create/dispose a context per operation. Queries return materialized DTOs, not tracked entities or deferred queries. A mutation uses one context and transaction for its business changes and audit entry. Concurrent component operations must not share a context.

### E1-S4 — Deterministic synthetic seeding
**Story:** As an evaluator, I want the app to start with realistic synthetic cases and demo users, so that I can try paging, filtering, sorting and the workflow straight away.
**Acceptance criteria:**
- Given a freshly migrated DB, when seeding runs, then every registered `ISeedContributor` runs in `Order`, and the synthetic business fixtures and demo credentials are identical on every clean start. Identity security salts and stamps need not be byte-identical.
- Given an already seeded DB, when seeding runs again, then no duplicate data is created.
- Given the seed data, when I inspect it, then it's all synthetic: no real names, SSNs or VA data.
**Technical notes:** The migrator explicitly runs migrations, then resolves and executes seed contributors in a fresh DI scope. Do not invoke UserManager-based contributors recursively from EF configuration callbacks. Contributors: E3 (roles, demo users), E2 (cases across every status, with audit entries). Use `Bogus` with a fixed seed (`Randomizer.Seed = new Random(20261016)`) and fixed UTC base timestamps. Each contributor owns its transaction and context. Identity contributors use the context underlying their UserManager/RoleManager stores; never wrap them in an unrelated context transaction.
**Testing:** integration test: seed twice, then assert the row counts are stable. Seed two fresh DBs and compare stable business data, excluding Identity password hashes, security stamps, generated IDs and other intentionally random infrastructure values.
**Traces to:** R-4.1-2, R-4.5-7

Prove two startup cases: fresh volumes and existing volumes after user edits/uploads. Repeat migrate/seed/start without duplicates, resetting passwords, or overwriting user edits. Seed with stable fixture keys, not just an `Any()` check that skips partially completed work. Identity salts, password hashes and security stamps are intentionally random and excluded from fixture equality checks. Handle contributor failures with a nonzero migrator exit. Do not wrap Identity work in a transaction owned by an unrelated context.

### E1-S5 — UTC time handling
**Story:** As a reviewer, I want every stored timestamp to be UTC and enforced by tooling, so that times are unambiguous and the spec's time rule holds.
**Acceptance criteria:**
- Given any entity with a timestamp, when it's saved, then the column type is `timestamptz`, application reads use UTC, and the database session uses UTC for textual timestamp output.
- Given code that calls `DateTime.Now`, `DateTime.Today` or `DateTimeOffset.Now`, when it builds, then compilation fails (banned API).
- Given a non-UTC `DateTimeOffset` reaches `SaveChanges`, when it's saved, then it's normalized to UTC by a model-wide convention.
**Technical notes:** register `TimeProvider.System`. `ConfigureConventions` adds a `DateTimeOffset` → UTC value converter. Use `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`) in tests. The UI displays times with an explicit "UTC" label (OQ-19).
**Testing:** unit test of the converter. Integration test reading back raw `timestamptz` values. Model test scanning all persisted DateTime/DateTimeOffset properties, including nullable Identity lockout/token fields and audit/attachment data, asserting UTC-compatible mappings. Apply DateTime UTC validation as well as DateTimeOffset normalization; Identity fields cannot be omitted.
**Traces to:** R-4.5-10

### E1-S6 — Structured logs and health checks
**Story:** As an operator, I want structured logs with operation IDs and health endpoints, so that I can see what the app is doing and diagnose failures.
**Acceptance criteria:**
- Given any HTTP request or Blazor circuit action, when it's logged, then the log is JSON on stdout with timestamp (UTC), level, category, EventId and operation ID; include TraceId/SpanId when an activity exists. Create an operation scope for interactive actions so correlation never depends on an HTTP request.
- Given a key domain operation (case created/updated/transitioned, attachment uploaded/downloaded, lookup called, login success/failure), when it happens, then a structured log event with the agreed EventId range is written (each epic emits its own; E1 provides the plumbing).
- Given a log or trace contains an SSN-shaped value, when it's emitted, then verification fails; structured logging must omit sensitive input, with redaction as an additional safeguard.
- Given the stack is running, when I GET `/health/live` and `/health/ready`, then live returns 200, and ready returns 200 only if the DB and the veteran-lookup service are reachable.
**Technical notes:** `CaseIntake.ServiceDefaults` with `AddServiceDefaults()`: `AddJsonConsole` (UTC timestamps), `LoggerMessage` source-generated logging, OpenTelemetry instrumentation, metrics and an observability dashboard are optional and excluded from required story completion; all external exporters stay disabled by default. `AddHealthChecks().AddNpgSql().AddUrlGroup(veteran-lookup /health)`. `Microsoft.Extensions.Compliance.Redaction` for SSN data classification. Full production monitoring is out of scope (4.7) and recorded in limitations.
**Testing:** a unit test with a captured `ILogger` (`FakeLogger` from `Microsoft.Extensions.Diagnostics.Testing`) asserting EventIds and redaction. An integration test for the health endpoints.
**Traces to:** R-4.2-14

Do not log SSNs, deterministic hashes of SSNs, passwords, request/response bodies, veteran names/addresses, or attachment content. Redaction packages do not automatically scrub arbitrary log strings and telemetry. Use an allowlist of safe structured fields, disable HTTP body logging, and test captured application and HTTP-client logs on success and failure paths.

### E1-S7 — Test harness and `tests` compose service
**Story:** As an evaluator, I want `docker compose run --rm tests` to run the complete automated suite, so that I can check quality with one command.
**Acceptance criteria:**
- Given the repo root, when I run `docker compose run --rm tests`, then every test project (unit, bUnit, integration, OpenAPI conformance, E2E/a11y) runs, a summary prints, and the exit code is non-zero if any test fails.
- Given tests need a DB, when the tests service starts, then it creates a unique test database per concurrent fixture, separate from demo data, and cleans it up after disposal.
- Given a developer machine with the .NET 10 SDK and Docker, when they run `dotnet test`, then the same suites run locally after the README’s browser setup helper completes. The Compose path requires no manual browser setup.
- Given startup has completed and egress is blocked, when the required test command runs, then its already-built image discovers every suite, performs no restore/build/download, and emits project-specific results. A deliberately failing test or missing/zero-test project produces a nonzero exit.
**Technical notes:** `tests` service uses the shared evaluation image built by ordinary startup, as defined in E1-S2. Pin the Playwright image version to the `Microsoft.Playwright` package version and retain its browser path. No browser installation runs at container startup. `depends_on: db, veteran-lookup (healthy)`. Entry point `dotnet test CaseIntake.sln -c Release --no-build --no-restore --logger trx --results-directory /results`. Build all test projects in Release during image creation. The suite wrapper checks expected project discovery and rejects zero tests. Add test projects to the repository/solution only when their first real tests exist; at G0 the wrapper validates the currently implemented foundation suite, and at G3/G4 it validates all required suites. xUnit v2 with `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio`, using VSTest through standard `dotnet test`. Do not set a Microsoft.Testing.Platform runner in `global.json`. Pin mutually compatible versions during E1-S1. `TestKit`: `CaseIntakeWebFactory : WebApplicationFactory<Program>` (in-process, with .NET 10 `UseKestrel()` for real-socket Playwright tests), `Respawn` for DB reset, `TestAuthHandler` with `WithUser(role)`, builders, `FakeTimeProvider`, and in-memory fakes of every contract.
**Testing:** a suite wrapper enumerating every `IsTestProject` in the repository and comparing it with the unfiltered solution; parse a separate TRX per project and fail if any project is missing, skipped wholesale, or discovers zero tests. A purposely failing sample, run once in a PR, proves the non-zero exit code.
**Traces to:** R-4.2-13, R-4.5-8

Put `tests` behind a Compose profile so ordinary startup does not launch the suite. Explicitly targeting `docker compose run --rm tests` must still start its healthy dependencies and run every suite. Build its browser binaries, OS dependencies, and .NET outputs into the image. At runtime use the selected runner's supported no-restore/no-build options and disable package/tool/browser downloads. Do not mix Microsoft.Testing.Platform and VSTest flags. Use the VSTest setup above and prove test discovery, result files, failure propagation, and that zero discovered tests is a failure. The suite must run on Linux amd64.

Give each concurrent integration fixture a unique database or serialize all fixtures sharing a resettable database. Resetting a shared database between classes is unsafe when classes run concurrently. Browser tests use a separate application host/database and temporary attachment root, with the real Identity flow and real local lookup service. Test setup creates required databases automatically and never resets evaluator demo data. A local `dotnet test` path must document browser prerequisites or use an automated helper; the evaluator's required Compose command has no such prerequisites beyond Docker.

### E1-S8 — Technical decision documentation, versions and README
**Story:** As an evaluator, I want concise decision records covering choices, alternatives, tradeoffs, assumptions and limitations, so that I can judge the engineering reasoning.
**Acceptance criteria:**
- Given `docs/adr/`, when I open it, then there's an index plus ADRs (MADR template: context, decision, alternatives, tradeoffs, consequences) for at least: .NET/Blazor/EF Core versions (E1), render mode (E5), data access and DB engine (E1), auth and roles (E3), workflow design (E2), audit approach (E2), attachment storage (E4), veteran lookup mock/client (E4), telemetry (E1), test strategy (E1), accessibility approach (E5).
- Given `docs/assumptions-and-limitations.md`, when I read it, then it lists every assumption (A1–A12), every specification ambiguity with its adopted decision, and the out-of-scope items from 4.7 as limitations.
- Given `docs/versions.md`, when I read it, then it names the exact .NET SDK/runtime, ASP.NET Core/Blazor and EF Core (and Npgsql provider) versions, matching `global.json`, `Directory.Packages.props` and the built image’s runtime inventory.
- Given `README.md`, when I read it, then it explains the startup command, test command, URL, demo accounts per role, and the repo layout.
- Given the reviewed repository, when G3/G4 is recorded, then `docs/traceability.md` maps every current requirement to implementation/test evidence, `docs/verification.md` records revision and command/manual results, and README links all deliverable documents in section 9.1.
**Technical notes:** each epic owner writes the ADRs in their area. E1 owns the template, index and review. A test reads `Directory.Packages.props` and checks `versions.md` matches (prevents drift).
**Testing:** a doc-consistency test comparing SDK/package pins and recorded `dotnet --info`/`dotnet --list-runtimes` from the evaluation image (versions). A link check on `docs/` (optional, in the tests service).
**Traces to:** R-4.2-15, R-4.5-2, R-4.5-3, R-4.5-4

### E1-S9 — Government requirement change and final submission readiness
**Story:** As the offeror, I want a defined process to absorb the Government-provided requirement change without regressing anything, so that the final repository submission meets both the original spec and the change.
**Acceptance criteria:**
- Given the change is received, when it's analyzed, then an impact note (`docs/requirement-change.md`) maps it to the affected epics and stories, and new stories are added to this plan and to the traceability table.
- Given the change is implemented, when `docker compose run --rm tests` runs, then all original tests plus the new ones pass.
- Given the final repository, when it's evaluated, then `docker compose up --build` still builds, migrates and seeds with no manual configuration.
**Technical notes:** the module boundaries and additive contracts are there so a change touches as few epics as possible. The unknown change has no preassigned effort estimate. G3 can complete before receipt; G4 cannot.
**Testing:** the full regression suite plus new tests for the change.
**Traces to:** R-4.1-5

</details>

---

<details>
<summary><span style="color: cyan"><b>EPIC 2 — Case Domain, Intake, Query & Status Workflow (Dev 2)</b></span></summary>

**Goal:** Implement the case data model, intake validation and audit, detail and edit, list paging/filtering/sorting and the controlled status workflow, through `ICaseService`, with Domain workflow rules and the shared persistence audit helper.

**Scope:** `CaseIntake.Domain/Cases`, `CaseIntake.Cases`, `Persistence/Configurations/Cases`, the case seed contributor, workflow and audit ADRs.
**Out of scope:** UI (E5), role definitions (E3; E2 only checks permissions through `ICurrentUser`), attachments (E4).

**Dependencies / interfaces:**
- *Provides:* `ICaseService`, `ICaseWorkflow`, `AuditAppender` implementation in Persistence (used by E4), the `Case` entity and the `Cases` table.
- *Consumes:* `ICurrentUser.GetAsync` (E3; test fake only until integration), `TimeProvider` and `AppDbContext` (E1), `VeteranSnapshot` data from the UI (sourced from E4's client).

### E2-S1 — Case entity and persistence mapping
**Story:** As a system, I want a Case entity that enforces the required fields and rules, so that case data stays valid at the storage layer too.
**Acceptance criteria:**
- Given a new case is saved, when the DB assigns the ID, then `Id` is a positive 64-bit integer (`bigint` identity) generated by the system.
- Given an existing case, when any code tries to change `Id`, then it can't (no public setter, and EF marks it `ValueGeneratedOnAdd` with `PropertySaveBehavior.Throw` after save).
- Given the schema, when I inspect it, then `Title` is `varchar(200) NOT NULL` with a check constraint (length ≥ 5), `Description` is nullable `varchar(4000)`, `Status` is stored as an integer constrained to 0–4, the audit columns are `timestamptz`, and there's a concurrency token.
**Technical notes:** `Case` has private setters plus domain methods (`Create`, `Edit`, `TransitionTo`). `builder.Property(c => c.Id).UseIdentityAlwaysColumn()` and `HasCheckConstraint("CK_Case_Id_Positive", "\"Id\" > 0")`. `uint Version` mapped to Postgres `xmin` with `.IsRowVersion()` (Npgsql). Optional added fields (allowed by 4.4): `VeteranName`, `VeteranAddress`, `VeteranSsnLast4`. Indexes on `Status`, `CreatedAtUtc`, `UpdatedAtUtc`. Add a `pg_trgm` GIN index on `Title` only if search performance needs it (optional).
**Testing:** unit tests on domain methods. An integration test proving `Id` > 0, immutability (an update attempt throws), and that DB constraints reject a 4-char and a 201-char title and a 4001-char description.
**Traces to:** R-4.4-1, R-4.4-2, R-4.4-3, R-4.4-4, R-4.5-4

### E2-S2 — Intake validation rules
**Story:** As a case worker, I want clear validation when I enter case data, so that I can fix mistakes before submitting.
**Acceptance criteria:**
- Given a title shorter than 5 or longer than 200 characters, or empty, when I submit, then I get a `Validation` error with a field error on `Title` that states the rule.
- Given a description over 4,000 characters, when I submit, then I get a field error on `Description`.
- Given an empty description, when I submit, then it's accepted (optional).
- Given the same request, when it's validated on the client (Blazor form) and on the server (service), then both apply exactly the same rules.
- Given a snapshot, when its strings exceed name 200/address 500 scalar values or its last-four field is not four ASCII digits, then creation returns field-specific Validation without persistence.
**Technical notes:** `CaseFieldLimits` and pure `CaseTextRules` live in Domain. A request validator in Contracts maps their results to field errors. Normalize Title before validation. Intake/edit services always invoke the validator; Blazor maps its field errors through `ValidationMessageStore`. Do not duplicate rules in UI annotations.
**Testing:** a parameterized xUnit `[Theory]` over boundary values (0, 4, 5, 200, 201 chars; 4000/4001).
**Traces to:** R-4.2-4, R-4.4-2, R-4.4-3, R-4.2-12

Use one normalization/validation routine shared by intake, edit, and UI validation. Trim Title before applying required and 5–200 Unicode scalar-value limits. Do not trim or truncate Description silently; allow null/empty and enforce its 4,000-scalar limit. DataAnnotations on a separate UI model do not inherit annotations from request records. Invoke the shared validator and map its field errors. Test whitespace-only titles, leading/trailing whitespace, supplementary Unicode, and both length boundaries. Count Unicode scalar values with `EnumerateRunes`, not UTF-16 `string.Length`; reject malformed UTF-16 and NUL before persistence. For supported text, PostgreSQL `char_length` and the service count agree. Test a title containing five supplementary characters so the DB minimum cannot reject a valid service result.

### E2-S3 — Create case (intake) with audit information
**Story:** As a case worker, I want to submit a new case intake, so that it enters triage with a record of who created it and when.
**Acceptance criteria:**
- Given valid input and the `cases.create` permission, when I create a case, then it's saved with status `New`, a system-generated ID, `CreatedAtUtc = UpdatedAtUtc = now (UTC)`, `CreatedBy = UpdatedBy = current user`, and a `Created` audit entry.
- Given a `VeteranSnapshot` is supplied, when the case is created, then the name, address and SSN last-4 are saved, and the full SSN is never stored.
- Given a user without `cases.create`, when they call create, then the result is `Forbidden` and nothing is saved.
- Given creation succeeds, when I check the logs, then an `CaseCreated` event (EventId 2001) is written with the case ID and actor, without description content.
**Technical notes:** `CaseService.CreateAsync` runs in a single `SaveChangesAsync` (case plus audit entry). Use `TimeProvider.GetUtcNow()` and `ICurrentUser`.
**Testing:** unit tests with fakes (`FakeTimeProvider`, a fake current user). An integration test against Postgres.
**Traces to:** R-4.2-4, R-4.2-5, R-4.3-1 (initial state), R-4.5-10

### E2-S4 — Audit trail (append-only)
**Story:** As a supervisor, I want an append-only history of what changed on a case, by whom and when, so that intake and triage decisions are traceable.
**Acceptance criteria:**
- Given any create, changed edit, status change or attachment upload, when it commits successfully, then one audit entry is appended with UTC time, actor, action, from/to status (if applicable) and a short details string (e.g. changed field names).
- Given the audit table, when an update or delete is attempted through the application, then it isn't possible (no update/delete API, and the SaveChanges guard rejects modified or deleted audit entries).
- Given a case ID, when `GetAuditTrailAsync` is called by a user with `cases.view`, then entries come back oldest to newest.
**Technical notes:** `CaseAuditEntry` (`bigint` identity, FK to Case). The operation owns its context and transaction. E2 and E4 use `AuditAppender.Append` from section 0.3. The helper never saves independently; a circuit-scoped context is forbidden. Details never contain SSNs or full descriptions.
**Testing:** unit tests for the change detection that produces the details string. Integration tests asserting entries for each action type.
**Traces to:** R-4.2-5

Append-only audit behavior needs an explicit implementation, such as a SaveChanges guard rejecting modified/deleted audit entities. EF property save behavior alone does not prohibit deleting a row. This protects application operations; protection against a database administrator is outside this challenge.

### E2-S5 — Case list query: paging, filtering, sorting
**Story:** As a case worker, I want to page through, filter and sort the case list, so that I can quickly find the cases I need to triage or work.
**Acceptance criteria:**
- Given 60 seeded cases and `PageSize=25`, when I request page 2, then I get items 26–50 and `TotalCount=60`. Page 3 has 10 items. A page past the end returns an empty list (not an error).
- Given `PageSize` outside 1–100, or `Page` < 1, when I query, then I get a `Validation` error.
- Given `Statuses=[Triaged, InProgress]`, when I query, then only cases in those statuses come back.
- Given `Search="water"`, when I query, then only cases whose Title or Description contains "water" (case-insensitive) come back.
- Given `SortBy` = any of Id/Title/Status/CreatedAt/UpdatedAt in either direction, when I query, then results come back in that order, with `Id` as a stable tiebreaker.
- Given filters and sort combined, when I query, then paging applies after filtering and sorting.
**Technical notes:** server-side `IQueryable` with `AsNoTracking()`. Sort goes through an allowlisted `switch` (never dynamic strings). `EF.Functions.ILike` with escaped wildcards. Count and page queries. Adopted filter set (status + text search) and page sizes (10/25/50) are recorded under OQ-4.
**Testing:** integration tests with a deterministic dataset covering every sort field and direction, filter combinations, boundaries and wildcard-escape safety (`%`, `_`). A performance check (10k rows, under 200 ms locally) is optional.
**Traces to:** R-4.2-1, R-4.2-2, R-4.2-3

Reject invalid enum values, Page below one, PageSize outside 1–100, and offsets that overflow the pagination arithmetic. UI choices remain 10/25/50. Use deterministic ordering with an explicit Id tiebreaker and define status order as New, Triaged, In Progress, Resolved, Closed. For an Id primary sort, no second Id ordering is needed. Empty filter results have an accessible empty state; changing filters resets to page one.

### E2-S6 — Case detail retrieval
**Story:** As a case worker, I want to open a case and see all its details, audit information and allowed next steps, so that I understand its state before acting.
**Acceptance criteria:**
- Given an existing case and the `cases.view` permission, when I get it, then I receive every field, the audit fields, the `Version`, the veteran snapshot (if any), and `AllowedNextStatuses` filtered by both the workflow and my permissions.
- Given a non-existent ID, when I get it after permission checks, then the result is `NotFound`.
**Technical notes:** `AllowedNextStatuses = workflow.NextStatuses(s).Where(t => actor.HasPermission(Permissions.Transition(s, t)))`.
**Testing:** unit tests for the allowed-next computation per role (using the fake current user). An integration test for NotFound.
**Traces to:** R-4.2-6, R-4.2-11

### E2-S7 — Case edit with immutable ID and optimistic concurrency
**Story:** As a case worker, I want to edit a case's title and description safely, so that I can correct information without overwriting a colleague's change.
**Acceptance criteria:**
- Given valid changes, the current `Version` and `cases.edit`, when I update, then Title/Description are saved, `UpdatedAtUtc` and `UpdatedBy` are refreshed, a new `Version` is returned, and an `Edited` audit entry lists the changed fields.
- Given a stale `Version`, when I update, then the result is `Conflict` and nothing is overwritten.
- Given an update request, when it's processed, then the ID and status can't be changed through edit (the ID comes from the route and isn't part of the request; status changes only through E2-S8).
- Given invalid values, when I update, then the same validation as intake applies (E2-S2).
- Given a case in `Closed`, when an edit is attempted, then it's rejected with `Validation` error with code `CaseClosed` (*adopted decision, recorded under OQ-9*).
**Technical notes:** Compare submitted `Version` with the loaded version and set the concurrency property’s original value to the submitted token before saving. Catch `DbUpdateConcurrencyException` and map it to `ErrorKind.Conflict`. A freshly loaded token alone cannot detect a stale browser form. No-op edits (no changes) don't write audit entries.
**Testing:** an integration test with two contexts simulating a concurrent edit, plus a stale browser token submitted after another operation has already committed. Unit tests for the changed-field detection.
**Traces to:** R-4.2-7, R-4.4-1, R-4.2-5

All edit/status mutations compare the submitted version and return a conflict without committing an audit entry when stale. Add concurrent edit-versus-transition tests, not only edit-versus-edit. A Closed case shows read-only edit behavior matching the service decision. No-op edits return the current DTO without changing timestamps, version or audit. Workflow tests cover all 25 pairs and invalid enum values. End-to-end coverage must exercise all five allowed edges, including the reopen branch.

### E2-S8 — Controlled status workflow
**Story:** As a supervisor, I want case status to change only along the approved workflow, so that cases follow the agreed triage process.
**Acceptance criteria:**
- Given each allowed transition (New→Triaged, Triaged→InProgress, InProgress→Resolved, Resolved→Closed, Resolved→InProgress), when a user with the matching transition permission requests it with the current `Version`, then the status changes, the audit fields update, and an audit entry records from/to: `Reopened` for Resolved→InProgress, otherwise `StatusChanged`.
- Given any other pair (e.g. New→Resolved, Closed→anything, InProgress→Triaged, same status→same status), when it's requested, then the result is `InvalidTransition` and nothing changes.
- Given an allowed transition but a user without the permission, when it's requested, then the result is `Forbidden`.
- Given a stale `Version`, when it's requested, then the result is `Conflict`.
- Given Resolved→InProgress, when it succeeds, then the audit action is recorded as `Reopened`.
**Technical notes:** Domain-owned `CaseWorkflow` is an immutable `FrozenDictionary<CaseStatus, ImmutableArray<CaseStatus>>` (the single source of truth, also used by the UI through `AllowedNextStatuses`). `Case.TransitionTo(target, workflow)` guards in the domain. Log `CaseStatusChanged` (EventId 2003).
**Testing:** an exhaustive `[Theory]` over all 25 from/to pairs asserting allowed/denied. Per-role permission tests. A concurrency integration test.
**Traces to:** R-4.2-8, R-4.3-1, R-4.3-2, R-4.3-3, R-4.3-4, R-4.3-5

### E2-S9 — Case seed contributor
**Story:** As an evaluator, I want a synthetic set of cases in every status, so that list features and workflow transitions are demonstrable right away.
**Acceptance criteria:**
- Given a fresh start, when seeding runs, then at least 60 synthetic cases exist (more than two pages at the default size) spread across all five statuses, each with a consistent audit trail and UTC timestamps.
- Given seeding re-runs, when cases already exist, then nothing is duplicated.
**Technical notes:** `CasesSeedContributor : ISeedContributor` (Order 20, after Identity). Bogus with a fixed seed. Generic, clearly fictional titles and descriptions.
**Testing:** an integration test counting per status and checking determinism (via E1-S4).
**Traces to:** R-4.1-2, R-4.5-7

</details>

---

<details>
<summary><span style="color: cyan"><b>EPIC 3 — Identity, Roles & Authorization (Dev 3)</b></span></summary>

**Goal:** Provide local, synthetic-only authentication and at least two roles with different permissions, enforced in UI, endpoints and services.

**Scope:** ASP.NET Core Identity setup, login/logout pages (static SSR), roles, the role→permission map, authorization policies, `ICurrentUser`, the demo-user seed, security-related logging, the auth ADR.
**Out of scope:** production identity / SSO (4.7), Government identity services (4.1).

**Dependencies / interfaces:**
- *Provides:* `ICurrentUser`, policy names = `Permissions.*` constants, `AppUser`/`AppRole` implementation in Persistence (shells in the foundation PR), `AddIdentityModule()`, the `Components/Account` pages, `TestAuthHandler` role presets for TestKit.
- *Consumes:* `AppDbContext` (E1). The permission constants are used by E2/E4/E5.

### E3-S1 — Local authentication with ASP.NET Core Identity
**Story:** As a user of the evaluation app, I want to sign in with a local synthetic account, so that I can use the app without any Government or external identity service.
**Acceptance criteria:**
- Given valid seeded credentials, when I sign in on `/account/login`, then I'm redirected to the case list and my display name and role are shown.
- Given invalid credentials, when I sign in, then I see a generic error message (no account enumeration), focus moves to it, and a `LoginFailed` event is logged without the password.
- Given I'm signed in, when I sign out, then my cookie is cleared and protected pages redirect to login.
- Given an unauthenticated request to any app page or the attachment endpoint, when it's made, then it's redirected to login (UI) or gets 401 (endpoint).
**Technical notes:** `AddIdentityCore<AppUser>().AddRoles<AppRole>().AddEntityFrameworkStores<AppDbContext>().AddSignInManager()`, cookie auth. The Blazor template's static-SSR account pages are trimmed to Login/Logout (no self-registration; adopted, OQ-18). Fallback authorization policy requires authenticated users. `AddDataProtection().SetApplicationName("CaseIntake").PersistKeysToFileSystem(...)` targets `/data/keys` on the `dataprotection` named volume so cookies survive container recreation. Identity EF stores resolve a request-scoped context for static SSR account requests; circuit business services use only `IDbContextFactory`. Never inject UserManager or a scoped context into long-lived interactive services. Cookie `SameSite=Lax`, `HttpOnly`. HTTP on localhost:8080 as the spec requires. `AuthenticationStateProvider` revalidation (template `IdentityRevalidatingAuthenticationStateProvider`).
**Testing:** integration tests (WebApplicationFactory) for login success/failure, redirect and 401. bUnit/E2E for the login form is in E5-S9.
**Traces to:** R-4.1-2, R-4.2-11, R-4.7 (production identity not used)

### E3-S2 — Roles and permission matrix
**Story:** As a supervisor, I want different roles to have different permissions, so that only appropriate users can perform sensitive workflow actions.
**Acceptance criteria:**
- Given the adopted matrix below, when permissions are evaluated, then each role gets exactly its listed permissions, and the two roles differ. For multiple roles, permissions are the union; unknown roles grant none.
- Given the matrix, when I read `docs/adr/xxxx-roles-and-permissions.md`, then the matrix is documented as an assumption recorded under OQ-3.

| Permission | CaseWorker | Supervisor |
|---|---|---|
| cases.view / cases.create / cases.edit | ✔ | ✔ |
| veteran.lookup | ✔ | ✔ |
| attachments.upload / attachments.download | ✔ | ✔ |
| transition New→Triaged | – | ✔ |
| transition Triaged→InProgress | ✔ | ✔ |
| transition InProgress→Resolved | ✔ | ✔ |
| transition Resolved→Closed | – | ✔ |
| transition Resolved→InProgress (reopen) | – | ✔ |

**Technical notes:** Keep `RolePermissionMap` in Contracts.Security so the lookup client, Identity adapters and authorization handler share it without referencing another feature. Use a `PermissionRequirement` handler that derives permissions from trusted role claims. `OperationActor` adapters use the same map. Register policies for all fixed permissions and all five transition names. Do not rely on claims transformation to populate circuit permissions.
**Testing:** a `[Theory]` over role × permission asserting the matrix exactly (a golden test, so any change is deliberate).
**Traces to:** R-4.2-11

### E3-S3 — `ICurrentUser` for Blazor circuits and endpoints
**Story:** As a developer of the domain services, I want one `ICurrentUser` abstraction, so that services can check identity and permissions in the same way whether they're called from an interactive component or an HTTP endpoint.
**Acceptance criteria:**
- Given an interactive Server component, when a service resolves `ICurrentUser`, then it reflects the circuit's authenticated user (not a stale or absent `HttpContext`).
- Given a minimal API endpoint, when `ICurrentUser` is resolved, then it reflects `HttpContext.User`.
- Given an anonymous context, when `GetAsync` returns an actor and the actor’s `HasPermission` is called, then it returns false.
- Given two simultaneous authenticated circuits and an HTTP download, when each resolves its actor, then each observes only its own principal and permissions; revalidation/logout prevents later unauthorized operations.
**Technical notes:** Separate circuit and HTTP adapters return an immutable `OperationActor` asynchronously. Never infer circuit identity from `HttpContext`. `UserId` is the Identity user ID; `DisplayName` is the user name.
**Testing:** unit tests with both sources mocked.
**Traces to:** R-4.2-11, R-4.2-5 (actor for audit)

Use two explicit `ICurrentUser` adapters selected by composition root: the web circuit scope uses the circuit adapter; download endpoints resolve the HTTP adapter explicitly and pass it to a request-scoped service instance. Both implement `GetAsync` and return `OperationActor`. Interactive operations obtain the principal asynchronously from `AuthenticationStateProvider`; HTTP operations use the request principal. Both copy roles and permissions into immutable collections and construct the same actor snapshot for one service operation. Do not block on asynchronous authentication or cache a principal globally. Test separate simultaneous circuits, anonymous access, logout/revalidation, and HTTP downloads to prove identities do not cross operations.

### E3-S4 — Seeded synthetic demo users
**Story:** As an evaluator, I want ready-made demo accounts for each role, so that I can compare what each role can do during the demo.
**Acceptance criteria:**
- Given a fresh start, when seeding runs, then the roles exist and at least one user per role is created (e.g. `caseworker@example.test`, `supervisor@example.test`) with the password from `Seed:DemoPassword` (default set in compose, documented in README).
- Given re-seeding, when users exist, then no duplicates are created and passwords aren't reset.
**Technical notes:** `IdentitySeedContributor` (Order 10) uses `UserManager`/`RoleManager`. Use the `.test` TLD (reserved, never real).
**Testing:** an integration test asserting the users and role memberships.
**Traces to:** R-4.1-2, R-4.2-11, R-4.5-7

### E3-S5 — Authorization enforcement across UI, endpoints and services
**Story:** As a supervisor, I want permissions enforced everywhere, not just hidden in the UI, so that users can't bypass role restrictions.
**Acceptance criteria:**
- Given a CaseWorker, when they try New→Triaged through the service or a crafted UI action, then they get `Forbidden` (service) and the UI shows an access-denied message.
- Given a page needing a permission the user lacks, when they navigate to it, then they see an accessible "Access denied" page (HTTP 403 for static/endpoint requests).
- Given the attachment download endpoint, when it's called without `attachments.download`, then it returns 403.
- Given any authorization denial, when it happens, then an `AuthorizationDenied` warning event is logged (user, permission, resource id).
**Technical notes:** `@attribute [Authorize(Policy = Permissions.CasesCreate)]` on pages. `<AuthorizeView Policy=...>` for controls (used by E5). `RequireAuthorization(policy)` on endpoints. Services check `OperationActor.HasPermission` as defense in depth. Provide `AccessDenied.razor` (E5 styles it).
**Testing:** an integration matrix test: each role × each protected service operation and endpoint.
**Traces to:** R-4.2-11

Apply permission checks to every public service entry point, including case list, detail, audit retrieval, attachment list, and lookup. Hiding a button is insufficient. Add a test principal with no permissions, since both demo roles can read cases and download files. The browser end-to-end suite must authenticate with real seeded credentials; a test authentication handler is only for isolated authorization tests.

### E3-S6 — Request security baseline
**Story:** As a reviewer, I want sensible baseline protections on the local app, so that the demo reflects secure defaults even though production hardening is out of scope.
**Acceptance criteria:**
- Given a cookie-authenticated HTTP form POST requiring antiforgery, when the token is missing, then it's rejected.
- Given any response, when it's inspected, then it includes `X-Content-Type-Options: nosniff`, `Referrer-Policy` and a CSP compatible with Blazor Server.
- Given five consecutive failed password attempts on a seeded account, when the fifth fails, then the account is locked for five minutes; a correct password during that interval is rejected and this is logged.
**Technical notes:** `app.UseAntiforgery()` (template). Small security-headers middleware. Identity `LockoutOptions` with threshold five and five-minute duration; enable lockout on seeded users and pass `lockoutOnFailure: true`. Document limitations (no HTTPS on localhost:8080, as specified by 4.5).
**Testing:** integration tests for the headers, antiforgery and lockout.
**Traces to:** R-4.2-11 (supporting), R-4.2-15 (limitations documented)

Specify antiforgery expectations for cookie-authenticated HTTP POST forms/endpoints, including login/logout. Interactive circuit events are not HTTP form posts and need their appropriate authentication/authorization protections. Test both paths rather than requiring a nonexistent form token for every interactive event. Check that CSP permits the submitted local scripts and SignalR connection, and that login redirects accept only local return URLs.

</details>

---

<details>
<summary><span style="color: cyan"><b>EPIC 4 — Integrations: Veteran Lookup Service & Attachments (Dev 4)</b></span></summary>

**Goal:** Deliver the local Veteran Lookup mock service that conforms to the spec's OpenAPI, a resilient client for it, and attachment upload/download.

**Scope:** `CaseIntake.VeteranLookup.Mock` (own container), `CaseIntake.VeteranLookup.Client`, `CaseIntake.Attachments` (+ `Domain/Attachments`, `Persistence/Configurations/Attachments`), `Web/Endpoints/Attachments` for the download endpoint, ADRs for the mock/client and attachment storage.
**Out of scope:** enterprise malware scanning (4.7). Any real VA service (4.1/4.7). UI (E5).

**Dependencies / interfaces:**
- *Provides:* `IVeteranLookupClient`, `IAttachmentService`, `AttachmentRoutes`, the `veteran-lookup` Dockerfile and service, `AddVeteranLookupClient()`, `AddAttachmentsModule()`, `MapAttachmentEndpoints()`.
- *Consumes:* `AuditAppender` and the `Case` existence check (E2, via `AppDbContext`), `ICurrentUser`/permissions (E3), compose wiring and the `attachments` volume (E1).

### E4-S1 — Veteran Lookup mock service
**Story:** As an evaluator, I want a local, deterministic Veteran Lookup service that implements the provided OpenAPI definition, so that the application's lookup integration really works without any external or Government service.
**Acceptance criteria:**
- Given a seeded synthetic SSN, when `POST /veterans/lookup` is called with `{"ssn":"900-12-3456"}`, then it returns 200 with `{"name": "...", "address": "..."}`, the same result every time.
- Given an SSN not in the dataset, when it's looked up, then it returns 404.
- Given absent/malformed JSON or a missing, null, non-string or empty `ssn` (empty-string rejection is an adopted local rule), when it is called, then it returns 400 (*not listed in the OpenAPI responses; see OQ-5c*).
- Given the service, when I GET `/openapi.yaml`, then the provided definition is served verbatim. When I GET `/health`, then 200.
- Given the dataset, when I inspect it, then every SSN is a synthetic fixture with area number 900–999 and every name and address is fictional.
**Technical notes:** an ASP.NET Core minimal API (`WebApplication.CreateSlimBuilder`). Records with System.Text.Json source generation (camelCase `name`, `address`). Dataset in an embedded `veterans.json`, generated once by a seeded Bogus generator script (committed for determinism), 50 records including `900-12-3456` as a documented positive lookup and `999-99-9999` as a documented absent lookup. Every returned name/address fits the snapshot limits in E2-S2. Document a few demo SSNs in README. No SSN or SSN hash logging. Log only outcome, duration and a random operation ID. Own Dockerfile, port 8080 inside the compose network, **not** published to the host (optional).
**Testing:** `WebApplicationFactory<MockProgram>` tests for 200/404/400 and determinism.
**Traces to:** R-4.1-4, R-4.2-16, R-4.5-9, R-4.6-1, R-4.6-2, R-4.6-3

### E4-S2 — OpenAPI contract conformance tests
**Story:** As a reviewer, I want automated proof that the mock and the client conform to the provided OpenAPI definition, so that the integration is spec-accurate, not just "works on my machine".
**Acceptance criteria:**
- Given `docs/contracts/veteran-lookup.openapi.yaml` (verbatim from spec 4.6), when the conformance tests run, then the mock's 200 response bodies validate against the 200 schema, and 404 is returned for unknown SSNs.
- Given the client, when it serializes a request, then the body validates against the request schema (`ssn` required, string).
- Given the YAML in the repo, when it's compared with the mock's `/openapi.yaml` output, then they're identical.
**Technical notes:** Use direct assertions for this small contract: JSON media type, required string `ssn`, exact route/method, 200 string fields, and 404. Keep the supplied OpenAPI unchanged; do not add a schema-validator dependency.
**Testing:** part of `CaseIntake.VeteranLookup.Tests`, run in the `tests` service against both the in-process mock and the compose `veteran-lookup` service.
**Traces to:** R-4.6-1, R-4.2-16, R-4.2-13

Keep the specification's OpenAPI contract unchanged. The SSN format rule and 400 response are local validation decisions, because the supplied schema only requires a string. Document them separately. Test JSON request content type, exact route/method/property names, valid 200 payloads, and unknown-SSN 404. Do not assume an OpenAPI 3.0 schema can be passed directly to an arbitrary JSON Schema validator without dialect handling. A few direct contract assertions are sufficient for this small contract.

### E4-S3 — Resilient typed Veteran Lookup client
**Story:** As a case worker, I want the app to look up a veteran by SSN and handle every outcome gracefully, so that intake keeps working even if the lookup service misbehaves.
**Acceptance criteria:**
- Given the mock returns 200, when `LookupAsync` is called, then the result is `Found` with name and address.
- Given 404, when it's called, then the result is `NotFound`.
- Given an SSN that doesn't match `^(?:[0-9]{9}|[0-9]{3}-[0-9]{2}-[0-9]{4})$`, when it's called, then the result is `InvalidInput` and no HTTP call is made (*format rule adopted, OQ-5c; normalize nine digits to the dashed form before POST*).
- Given a timeout, 5xx or connection failure, when it's called, then it performs at most one retry for connection errors, HTTP 408, 429 or 5xx, within a five-second total budget, and returns `Unavailable`. Caller cancellation propagates separately.
- Given any call, when it's logged or traced, then the SSN isn't present (redacted), but the outcome and duration are.
- Given a user without `veteran.lookup`, when the lookup is invoked through the UI-facing service, then it's denied.
**Technical notes:** `services.AddHttpClient<IVeteranLookupClient, VeteranLookupClient>(c => c.BaseAddress = config["VeteranLookup:BaseUrl"])` + `.AddStandardResilienceHandler()` (`Microsoft.Extensions.Http.Resilience`; tune total timeout and retries). `PostAsJsonAsync` with the source-generated context. Health check registration for E1-S6. Optional meter counter `veteran_lookup_requests{outcome}`. Check `ICurrentUser.GetAsync` and shared role permissions in this client before sending HTTP; unauthorized calls return `Forbidden` without a network request.
**Testing:** unit tests with a stub `HttpMessageHandler` (200/404/500/timeout). An integration test against the mock in compose.
**Traces to:** R-4.2-16, R-4.6-1, R-4.2-14

The client must handle malformed JSON, missing or unusable name/address fields, unexpected status codes, timeout and connection errors. Map invalid local input to `InvalidInput`, denied access to `Forbidden`, and unusable responses or transport failures to `Unavailable`. Propagate caller cancellation without displaying a service outage. Retries are permitted only because this lookup POST is read-only; explicitly configure retryable statuses, attempt count, and a total time budget of five seconds. Test the budget without real sleeps where practical.

### E4-S4 — Attachment model and storage abstraction
**Story:** As a developer, I want attachment metadata in the database and file content in a pluggable store, so that uploads are durable across restarts and storage can be swapped.
**Acceptance criteria:**
- Given an attachment is stored, when I inspect the DB, then metadata (Guid Id, CaseId FK, original file name (sanitized), content type, size, SHA-256, storage key, `UploadedAtUtc`, `UploadedBy`) is saved, and the bytes are in the store under a server-generated key (never the user's file name).
- Given the containers restart, when I download a previously uploaded file, then it's still available (named volume `attachments`).
- Given configuration, when `Attachments:MaxBytes` and `Attachments:AllowedContentTypes` are set, then the service uses them (adopted defaults: 10 MiB (10,485,760 bytes); pdf, png, jpeg, txt, docx; OQ-6).
**Technical notes:** `IAttachmentStore` (internal to E4) with `FileSystemAttachmentStore` (root `/data/attachments`, sharded by key prefix) and an `InMemoryAttachmentStore` for tests. Storage decision (filesystem vs DB `bytea`) documented in an ADR.
**Testing:** unit tests for the store (temp directory). An integration test for metadata mapping.
**Traces to:** R-4.2-9, R-4.2-10, R-4.5-10

### E4-S5 — Attachment upload and list
**Story:** As a case worker, I want to upload files to a case and see the attachment list, so that supporting documents are kept with the case.
**Acceptance criteria:**
- Given a case, `attachments.upload` and a file within limits, when I upload, then it's streamed to the store, its metadata is saved, an `AttachmentUploaded` audit entry is written using `AuditAppender.Append` in the upload operation’s context, and the new `AttachmentDto` is returned.
- Given a file over the size limit or of a disallowed type, when I upload, then I get `Validation` with a clear message, no metadata/audit is committed, and temporary content is removed or scheduled for recovery cleanup if removal fails.
- Given a non-existent case, when I upload, then I get `NotFound`.
- Given a failure after the bytes are written, when the DB save fails, then rollback removes metadata/audit and attempts blob deletion; deletion failure is logged safely and left for recovery cleanup.
- Given a case, when I list its attachments with `cases.view`, then I get them newest first.
- Given a file name with path segments or control characters, when it's saved, then the stored display name is sanitized (remove both slash styles, CR/LF and controls, then cap length at 200 characters; use `attachment` if empty).
- Given actual content length exceeds the maximum or differs from declaredSize, when streaming ends or limit is reached, then the upload returns Validation and leaves no committed metadata/audit. Temporary files are cleaned immediately when possible or by the recovery cleanup.
**Technical notes:** the service takes a `Stream` (the UI passes `IBrowserFile.OpenReadStream(maxAllowedSize)`). Count bytes while streaming and abort over the limit. Compute SHA-256 incrementally (`IncrementalHash`). Validate an explicit MIME/extension pair allowlist: application/pdf/.pdf, image/png/.png, image/jpeg/.jpg/.jpeg, text/plain/.txt, and application/vnd.openxmlformats-officedocument.wordprocessingml.document/.docx. Configure MIME types and extensions together and reject inconsistent startup configuration. Browser MIME/extension claims do not prove file safety; content scanning remains out of scope. Malware scanning is out of scope (4.7) and listed as a limitation.
**Testing:** unit tests (limits, sanitization, compensation with a failing fake DB). Integration tests for audit and list ordering.
**Traces to:** R-4.2-9, R-4.2-5, R-4.2-11

For upload, authorize and validate the case first; stream into a temporary file under the server-controlled storage root; enforce the actual byte limit; then promote the completed file to its final key and commit metadata plus audit in one database transaction. Dispose streams and remove temporary/final files on cancellation before commit or a known failed commit. If commit outcome is uncertain, check metadata using the server-generated attachment ID with a fresh context before deleting its final file. A database and filesystem cannot share an atomic transaction. Document the remaining crash window and provide a small startup or maintenance cleanup routine for old temporary files and final files with no committed metadata. Run cleanup in the migrator before web accepts uploads, while no upload operation is active. Never clean committed metadata files. Log cleanup failures and retry on next startup; retain an age grace period for temporary files.

Add fault tests for interrupted streams, declared length differing from actual length, limit overflow, storage failure, database failure, and cleanup failure. Uploads known to have failed before commit must leave no metadata or audit entry. An uncertain commit is reported as uncertain; refresh attachment state before retrying. Successful uploads retain downloadable bytes across container recreation. If commit status cannot be determined, retain the final file for startup reconciliation rather than risking deletion of committed content. A missing stored file returns a controlled 404 and a diagnostic event without exposing its path.

### E4-S6 — Attachment download endpoint
**Story:** As a case worker, I want to download a case attachment, so that I can review supporting documents.
**Acceptance criteria:**
- Given an attachment on case X and `attachments.download`, when I GET `/cases/X/attachments/{id}/content`, then I receive the original bytes (same SHA-256), the stored content type, `Content-Disposition: attachment with a safely encoded original display filename` and `X-Content-Type-Options: nosniff`.
- Given the attachment ID belongs to a different case, or doesn't exist, when I request it, then I get 404.
- Given an unauthenticated user or one without the permission, when they request it, then they get 401 or 403.
- Given an attachment response, when the server finishes writing its body successfully, then an `AttachmentDownloaded` log event (EventId 4xxx) is written.
- Given stored content is missing, when an authorized download is attempted, then the endpoint returns a controlled 404 and logs a safe storage diagnostic. Failed/cancelled responses must not be logged as completed downloads.
**Technical notes:** E4 owns the endpoint implementation under `Web/Endpoints/Attachments`, so HTTP composition may reference Identity and Attachments without feature-to-feature references. `MapAttachmentEndpoints()` explicitly constructs the attachment service with the HTTP actor adapter through a registered factory in Web. Keep circuit services registered with the circuit adapter. The endpoint uses `.RequireAuthorization(Permissions.AttachmentsDownload)`, and returns `TypedResults.File(stream, contentType, fileDownloadName, enableRangeProcessing: true)`. A plain anchor link from the UI is keyboard-accessible and works with Interactive Server (no JS interop needed).
**Testing:** integration tests: round-trip upload→download hash equality, cross-case 404, the 401/403 matrix.
**Traces to:** R-4.2-10, R-4.2-11, R-4.2-14

</details>

---

<details>
<summary><span style="color: cyan"><b>EPIC 5 — Blazor UI, Accessibility & End-to-End Experience (Dev 5)</b></span></summary>

**Goal:** Deliver a complete, keyboard-accessible Blazor UI for the primary workflow, with clear labels, validation, focus management and status/error messages, plus the end-to-end and accessibility test suite.

**Scope:** `CaseIntake.Web` components, pages and styles (excluding `Components/Account`, which E3 owns), the render-mode and accessibility ADRs, `CaseIntake.Web.Tests` (bUnit), `CaseIntake.E2E` (Playwright + axe).
**Out of scope:** business rules (calls `ICaseService` etc. only), authorization rules (uses policies from E3).

**Dependencies / interfaces:**
- *Consumes:* `ICaseService`, `AllowedNextStatuses` (without recomputing workflow rules), `IAttachmentService`, `AttachmentRoutes`, `IVeteranLookupClient`, `Permissions` policies, Domain `CaseFieldLimits`. Uses TestKit fakes only for isolated component work; replace them with real modules incrementally from G1.
- *Provides:* the reusable accessible component kit (used by E3's login page styling) and the E2E harness that proves the whole integration.

### E5-S1 — App shell, render mode, layout and navigation
**Story:** As a keyboard or screen-reader user, I want a consistent, well-structured layout, so that I can navigate the app efficiently.
**Acceptance criteria:**
- Given any page, when I press Tab first, then a visible "Skip to main content" link gets focus and moves focus to `<main>`.
- Given any page, when it loads or I navigate, then the document `<title>` is unique and descriptive, and focus moves to the page `<h1>`.
- Given the layout, when I inspect it, then it uses landmarks (`header`, `nav`, `main`), the nav is a labeled list of links, and the current page is marked `aria-current="page"`.
- Given the circuit disconnects, when reconnecting, then an accessible reconnect message is shown.
- Given an unknown route, when I open it, then an accessible Not Found page is shown.
- Given the render mode, when I read the ADR, then it explains Interactive Server vs WebAssembly vs Auto, with tradeoffs (latency, scalability, offline, auth, file streaming).
**Technical notes:** `AddRazorComponents().AddInteractiveServerComponents()`. Per-page `@rendermode InteractiveServer` (account pages stay static SSR). `<FocusOnNavigate RouteData="routeData" Selector="h1" />`. .NET 10 `ReconnectModal` component and the Router `NotFoundPage`. `<PageTitle>`. A visible focus style (≥ 3:1 contrast).
**Testing:** bUnit tests for the layout landmarks. E2E focus-after-navigation (E5-S9).
**Traces to:** R-4.2-12, R-4.5-1, R-4.5-3

### E5-S2 — Accessible form and feedback component kit
**Story:** As a user, I want every form field clearly labeled, errors explained next to the field and status changes announced, so that I can complete tasks without guessing.
**Acceptance criteria:**
- Given any input component in the kit, when rendered, then it has a programmatic `<label for>`, shows required state in text (not just color), and links help and error text with `aria-describedby`.
- Given a submit with errors, when validation fails, then a validation summary with links to each field appears at the top, gets focus, and each invalid field has `aria-invalid="true"` and an inline message.
- Given an operation succeeds, when it completes, then a message is announced through a polite live region (`role="status"`).
- Given an operation fails (service error, conflict, forbidden, lookup unavailable), when it completes, then an error message is shown in a `role="alert"` region.
**Technical notes:** components `LabeledInputText`, `LabeledTextArea` (with a character counter announced politely and debounced), `FormErrorSummary`, `StatusAnnouncer` (a scoped service + component), `ErrorAlert`. Use `ElementReference.FocusAsync()`. Map `Result.Error.FieldErrors` to `ValidationMessageStore`.
**Testing:** bUnit tests for attribute presence (label, aria-describedby, aria-invalid) and focus calls (bUnit JSInterop/`FocusAsync` verification).
**Traces to:** R-4.2-12

### E5-S3 — Case list page with paging, filtering and sorting
**Story:** As a case worker, I want a case list I can page, filter and sort with the keyboard, so that I can find cases quickly.
**Acceptance criteria:**
- Given the list page, when it loads, then a table with caption, column headers (ID, Title, Status, Created (UTC), Updated (UTC)) and the first page of cases is shown, using server-side paging.
- Given a sortable column header, when I activate it with Enter or Space, then the list re-sorts and the header shows `aria-sort` ascending/descending.
- Given the filter form (status checkboxes in a labeled `fieldset`, search text box), when I apply it, then results update, and a polite live region announces "Showing 1–25 of 42 cases".
- Given pagination controls, when I use Previous/Next/page-size with the keyboard, then pages change and the current page is announced. The controls are labeled ("Go to next page").
- Given filter, sort and page state, when I reload or share the URL, then the same view is restored (query string).
- Given no results, when the filter matches nothing, then a clear empty-state message is shown.
- Given a case title link, when I activate it, then I go to the case detail page.
**Technical notes:** Use a semantic HTML table with button-based sortable headers and explicit paging controls, calling `ICaseService.ListAsync`. This avoids introducing a grid dependency for the required table behavior. `[SupplyParameterFromQuery]` + `NavigationManager.GetUriWithQueryParameters`.
**Testing:** bUnit with the fake `ICaseService` (sort/filter/page parameters passed correctly, announcements). E2E keyboard sort and paging.
**Traces to:** R-4.2-1, R-4.2-2, R-4.2-3, R-4.2-12

### E5-S4 — Case intake page with Veteran Lookup
**Story:** As a case worker, I want an intake form that validates input and lets me look up a veteran by SSN, so that I can create accurate cases quickly.
**Acceptance criteria:**
- Given the intake page, when it loads, then Title (required, 5–200) and Description (optional, ≤ 4,000) fields with labels, hints and character counters are shown, and focus is on the `<h1>`.
- Given invalid input, when I submit, then nothing is saved, the error summary gets focus, and the field errors match the server rules exactly.
- Given the optional "Veteran lookup" section, when I enter an SSN and activate "Look up veteran", then the result is announced: Found (name and address shown, with "Use this veteran"), Not found, Invalid SSN format, Service unavailable (intake can continue without it), or Access denied.
- Given the SSN field, when it's rendered, then it's a labeled `type="text"` with `inputmode="numeric"`, `autocomplete="off"`, and its value isn't kept after the lookup and is never displayed after submission except the last 4.
- Given a valid submit, when the case is created, then I'm taken to its detail page, and a success status ("Case 123 created") is announced.
- Given a server-side failure, when I submit, then an error alert is shown and my input is preserved.
**Technical notes:** `EditForm` with `ValidationMessageStore` populated by the shared normalization/validation routine for `CreateCaseRequest`. `IVeteranLookupClient` injected. Any mapping of `VeteranSnapshot` into Domain uses primitives; Domain does not reference Contracts. Validate snapshot lengths and four ASCII digits server-side before saving. Disable double-submit while busy (`aria-busy`). Page requires the `Permissions.CasesCreate` policy.
**Testing:** bUnit (validation messages, focus, all five lookup outcomes with a fake client). E2E full intake via keyboard only.
**Traces to:** R-4.2-4, R-4.2-5, R-4.2-12, R-4.2-16, R-4.6-1

Clear the SSN after a completed lookup attempt. Capture the last four digits before clearing only if they are needed for the selected snapshot. Clear any previously selected veteran when a new lookup starts or fails, so stale results cannot be attached silently.

### E5-S5 — Case detail and edit page
**Story:** As a case worker, I want to view a case's details and edit them in place, so that I can keep the case up to date.
**Acceptance criteria:**
- Given a case, when I open `/cases/{id}`, then I see ID (read-only), title, description, status, veteran snapshot (last-4 only), Created/Updated at (UTC) and by, and the audit trail as an accessible list or table.
- Given `cases.edit`, when I activate "Edit", then the fields become editable, focus moves to Title, and Save/Cancel are available. Without the permission, or when status is Closed, no Edit button is shown.
- Given valid changes, when I save, then they're persisted, the view updates, and "Changes saved" is announced. Cancel restores the values and returns focus to the Edit button.
- Given someone else changed the case, when I save, then a conflict message explains this and offers "Reload latest" without losing my unsaved text silently.
- Given a non-existent ID, when I open it, then the Not Found page is shown.
**Technical notes:** `ICaseService.GetAsync/UpdateAsync/GetAuditTrailAsync`. `Version` is held in the form state. .NET 10 `NavigationManager.NotFound()` for missing cases.
**Testing:** bUnit (view/edit toggling, focus return, conflict UI). E2E edit by keyboard.
**Traces to:** R-4.2-6, R-4.2-7, R-4.2-5, R-4.2-12, R-4.4-1

### E5-S6 — Status transition controls
**Story:** As a supervisor or case worker, I want to see and use only the status changes I'm allowed to make, so that I follow the workflow without trial and error.
**Acceptance criteria:**
- Given a case, when the detail page renders, then a labeled "Change status" group shows one button per `AllowedNextStatuses` entry (e.g. "Move to Triaged", "Reopen (move to In Progress)"). If none, it shows the text "No status changes available".
- Given I activate a transition, when I confirm in an accessible confirmation dialog (focus trapped, Esc cancels, cancellation returns focus to the trigger), then the status updates, the audit trail refreshes, focus moves to the persistent status heading, and "Status changed to Triaged" is announced.
- Given the service returns `InvalidTransition`, `Forbidden` or `Conflict`, when that happens, then a clear error alert is shown and the displayed status is refreshed.
**Technical notes:** a native `<dialog>` with `showModal()` through a tiny JS module (for the built-in focus trap and Esc handling), or an equivalent Blazor component. Buttons come from server data, not duplicated UI rules.
**Testing:** bUnit per role (CaseWorker vs Supervisor see different buttons). E2E: Supervisor New→Triaged; CaseWorker can't close.
**Traces to:** R-4.2-8, R-4.3-1..5, R-4.2-11, R-4.2-12

### E5-S7 — Attachments panel (upload and download)
**Story:** As a case worker, I want to upload and download attachments from the case page using the keyboard, so that documents are kept with the case.
**Acceptance criteria:**
- Given the case detail page, when the attachments panel renders, then it shows a labeled file input with the allowed types and maximum size stated in text, an "Upload" button, and a table of existing attachments (name, size, uploaded at (UTC), by).
- Given a valid file, when I upload, then progress or busy state is announced, then "File uploaded" is announced, and the new row appears.
- Given an invalid file (too large or wrong type), when I upload, then an error alert explains why, and focus returns to the file input.
- Given an attachment row, when I activate its download link (named "Download <file name>"), then the file downloads from `AttachmentRoutes.Content(...)`.
- Given a user without the upload permission, when the panel renders, then the upload controls are hidden, but the list and download remain if permitted.
**Technical notes:** `<InputFile>` (Interactive Server streams over SignalR; set `OpenReadStream(maxAllowedSize: config)`). Configure the SignalR `MaximumReceiveMessageSize` only if needed (default chunking is fine). `AuthorizeView Policy=...`.
**Testing:** bUnit (`InputFile` test helpers in bUnit) for success and error messaging. E2E upload→download round-trip (Playwright `SetInputFilesAsync`, `RunAndWaitForDownloadAsync`).
**Traces to:** R-4.2-9, R-4.2-10, R-4.2-11, R-4.2-12

### E5-S8 — Role-aware UI, sign-in status and access-denied experience
**Story:** As a user, I want to see who I'm signed in as, what role I have, and a clear message when something isn't allowed, so that permissions are understandable.
**Acceptance criteria:**
- Given I'm signed in, when any page renders, then the header shows my display name, my role, and a keyboard-reachable "Sign out" button.
- Given I navigate to a page I'm not permitted to use, when it loads, then an accessible access-denied page with a link back to the case list is shown (`<h1>` focused).
- Given navigation links, when I lack a permission (e.g. create), then the corresponding link ("New case") isn't shown.
**Technical notes:** `<AuthorizeView>` with E3 policies. `CascadingAuthenticationState`. Style the E3 login/logout pages with the E5 kit for a consistent look.
**Testing:** bUnit with `TestAuthorizationContext` (bUnit) per role.
**Traces to:** R-4.2-11, R-4.2-12

### E5-S9 — End-to-end keyboard workflow and automated accessibility tests
**Story:** As an evaluator, I want automated end-to-end tests that drive the primary workflow through keyboard navigation and programmatic file selection and check accessibility, so that I have evidence the app works as a whole and meets the accessibility requirements.
**Acceptance criteria:**
- Given the `tests` service, when E2E runs, then a Playwright (Chromium, headless) test completes the full primary workflow **using keyboard navigation and programmatic file selection**: sign in → filter, sort and page the list → create a case (including a validation error and a veteran lookup) → edit → status transitions including reopen → upload and download an attachment → sign out.
- Given each page in the workflow, when the axe-core scan runs, then there are zero violations at the WCAG 2.x A/AA rule tags (false positives require documented evidence; known violations affecting required behavior block G3).
- Given each workflow step, when it completes, then the test asserts the expected status or error message text and the focus target.
- Given role differences, when E2E runs as each demo user, then the CaseWorker can't triage or close, and the Supervisor can.
- Given the full workflow suite, when it runs, then browser coverage exercises all five allowed transitions and rejects unauthorized triage/close/reopen actions. Manual review separately verifies native picker and download operation with keyboard.
- Given G3 preparation, when the walkthrough is reviewed, then `docs/demo.md` covers every demonstration item in section 9.1 using documented synthetic inputs and real application behavior.
**Technical notes:** `Microsoft.Playwright` + `Deque.AxeCore.Playwright`. The host comes from E1's `CaseIntakeWebFactory` with .NET 10 `UseKestrel()` (real socket, test DB, compose `veteran-lookup`). Traces and screenshots are saved to `/results` on failure. Keep the run under five minutes as a nonbinding performance goal.
**Testing:** this story *is* the test suite. Flaky-test policy: no fixed sleeps; use Playwright auto-wait and `Expect` assertions.
**Traces to:** R-4.2-12, R-4.2-13, R-4.1-3, R-4.2-16

Axe checks only a subset of WCAG and cannot establish WCAG 2.2 AA conformance. Use it as automated evidence, alongside manual keyboard review of the entire primary workflow and a screen-reader spot check of labels, validation and asynchronous messages. Include zoom/reflow, visible focus, empty/loading/error states, and reconnect behavior in the review record. Fix violations affecting the specified workflow; document any remaining limitation without treating a waiver as proof of compliance.

Browser upload automation through `SetInputFilesAsync` bypasses the native file picker. Describe the automated scenario accurately as keyboard navigation plus programmatic file selection, then manually verify native picker keyboard operation and download behavior. Do not claim the whole automated workflow uses keyboard input only. Test meaningful focus and announcements after service errors, conflicts and modal cancellation.

</details>

---

<details>
<summary><span style="color: cyan"><b>6. Story summary</b></span></summary>

| Epic | Stories |
|---|---|
| E1 Platform, persistence and delivery | 9 |
| E2 Case intake, query and workflow | 9 |
| E3 Identity and authorization | 6 |
| E4 Lookup and attachments | 6 |
| E5 UI and accessibility | 9 |
| Total | 39 |

E1-S9 remains conditional on the supplied Government change. The other 38 stories implement or verify the currently known scope.

Epic owners coordinate shared files through E1. Agent assignments follow the dependency gates; no elapsed-time or staffing estimate determines readiness.

</details>

---

<details>
<summary><span style="color: cyan"><b>7. Requirements traceability</b></span></summary>

| Req ID | Spec § | Requirement summary | Story IDs |
|---|---|---|---|
| R-4.1-1 | 4.1 | Self-contained Case Intake and Triage application | E1-S2, (all epics) |
| R-4.1-2 | 4.1 | Synthetic data only. No VA information, Government credentials, identity services or infrastructure | E1-S2, E1-S4, E2-S9, E3-S1, E3-S4, E4-S1 |
| R-4.1-3 | 4.1 | Live demo is a complete, functional implementation. No static screens, hard-coded demos or placeholders | DoD #3, E5-S9 (and all functional stories) |
| R-4.1-4 | 4.1 | The sample data service may be a local deterministic mock/stub | E4-S1 |
| R-4.1-5 | 4.1 | Final submission keeps all requirements and implements the Government-provided requirement change | E1-S9 |
| R-4.2-1 | 4.2 | Case list with paging | E2-S5, E5-S3 |
| R-4.2-2 | 4.2 | Case list with filtering | E2-S5, E5-S3 |
| R-4.2-3 | 4.2 | Case list with sorting | E2-S5, E5-S3 |
| R-4.2-4 | 4.2 | Case intake with validation | E2-S2, E2-S3, E5-S4 |
| R-4.2-5 | 4.2 | Case intake with audit information | E2-S3, E2-S4, E2-S7, E3-S3, E4-S5, E5-S4, E5-S5 |
| R-4.2-6 | 4.2 | Case detail | E2-S6, E5-S5 |
| R-4.2-7 | 4.2 | Case edit | E2-S7, E5-S5 |
| R-4.2-8 | 4.2 | Controlled status-workflow transitions | E2-S8, E5-S6 |
| R-4.2-9 | 4.2 | Attachment upload | E4-S4, E4-S5, E5-S7 |
| R-4.2-10 | 4.2 | Attachment download | E4-S4, E4-S6, E5-S7 |
| R-4.2-11 | 4.2 | At least two user roles with different permissions | E3-S1..S6, E2-S6, E2-S8, E4-S5, E4-S6, E5-S6, E5-S7, E5-S8 |
| R-4.2-12 | 4.2 | Keyboard-accessible primary workflow: clear labels, validation, focus behavior, status/error messages | E5-S1..S9, E2-S2 |
| R-4.2-13 | 4.2 | Automated tests | E1-S7, E4-S2, E5-S9 (+ testing in every story) |
| R-4.2-14 | 4.2 | Operational telemetry, such as logs | E1-S6, E4-S3, E4-S6 (+ events in E2/E3) |
| R-4.2-15 | 4.2 | Technical decision documentation: choices, alternatives, tradeoffs, assumptions, limitations | E1-S8, E3-S6 (+ ADRs from all epics) |
| R-4.2-16 | 4.2 | Working connection to the sample data service (4.6) | E4-S1, E4-S2, E4-S3, E5-S4, E5-S9 |
| R-4.3-1 | 4.3 | New → Triaged allowed | E2-S8, E5-S6 (initial status New: E2-S3) |
| R-4.3-2 | 4.3 | Triaged → In Progress allowed | E2-S8, E5-S6 |
| R-4.3-3 | 4.3 | In Progress → Resolved allowed | E2-S8, E5-S6 |
| R-4.3-4 | 4.3 | Resolved → Closed allowed | E2-S8, E5-S6 |
| R-4.3-5 | 4.3 | Resolved → In Progress (reopen) allowed | E2-S8, E5-S6 |
| R-4.4-1 | 4.4 | Case ID: positive 64-bit integer, system-generated, immutable | E2-S1, E2-S7, E5-S5 |
| R-4.4-2 | 4.4 | Title: required, 5–200 characters | E2-S1, E2-S2 |
| R-4.4-3 | 4.4 | Description: optional, max 4,000 characters | E2-S1, E2-S2 |
| R-4.4-4 | 4.4 | Offeror may add fields | E2-S1 (audit, veteran snapshot, version fields) |
| R-4.5-1 | 4.5 | ASP.NET Core Blazor Web App on a supported .NET LTS | E1-S1, E5-S1 |
| R-4.5-2 | 4.5 | Identify the exact .NET, Blazor and EF Core versions | E1-S1, E1-S8 |
| R-4.5-3 | 4.5 | Choose a render mode, and document the decision and tradeoffs | E5-S1, E1-S8 |
| R-4.5-4 | 4.5 | EF Core (or an equivalent, with justification) | E1-S1, E1-S3, E2-S1, E1-S8 |
| R-4.5-5 | 4.5 | Linux x86-64 containers, Docker Compose v2 | E1-S2 |
| R-4.5-6 | 4.5 | Endpoint http://localhost:8080 | E1-S2 |
| R-4.5-7 | 4.5 | `docker compose up --build` builds, starts, migrates and seeds with no manual configuration | E1-S2, E1-S3, E1-S4, E2-S9, E3-S4 |
| R-4.5-8 | 4.5 | `docker compose run --rm tests`; a `tests` service runs the complete suite | E1-S2, E1-S7 |
| R-4.5-9 | 4.5 | Network: registries only at build time. No runtime dependency on external services. Sample service is local | E1-S2, E4-S1 |
| R-4.5-10 | 4.5 | All stored timestamps in UTC | E1-S5, E2-S3, E4-S4 |
| R-4.6-1 | 4.6 | Connect to a service conforming to the OpenAPI: `POST /veterans/lookup`, required `ssn`, 200 {name, address}, 404 | E4-S1, E4-S2, E4-S3, E5-S4 |
| R-4.6-2 | 4.6 | Include a local mock/stub implementation | E4-S1 |
| R-4.6-3 | 4.6 | Only synthetic data, generated by the offeror | E4-S1 |
| R-4.7 | 4.7 | Out of scope: production identity, real VA data/services, cloud deployment, enterprise malware scanning, full production monitoring, production-scale proof | Not implemented. Recorded as limitations in E1-S8 (see E3-S1, E4-S5, E1-S6) |

</details>

---

<details>
<summary><span style="color: cyan"><b>8. Specification ambiguities and adopted decisions</b></span></summary>

OQ-1 is the only unknown external requirement. Other rows are adopted implementation choices and do not block agent assignments.

| ID | Question | Spec ref | Adopted implementation decision | Affects |
|---|---|---|---|---|
| OQ-1 | What is the "Government-provided requirement change", and when will it arrive? | 4.1 | Unknown external input. Process through E1-S9 on receipt; G4 stays pending until implemented and verified. | All |
| OQ-2 | Does "audit information" mean created/updated fields only, or a full change history? | 4.2 | Both (audit fields + append-only trail). | E2 |
| OQ-3 | Role names and the permission matrix aren't specified. | 4.2 | CaseWorker / Supervisor, matrix in E3-S2. | E2, E3, E5 |
| OQ-4 | Which fields can be filtered and sorted, and what page sizes and default sort? | 4.2 | Filter: status (multi) + text search on Title/Description. Sort: Id/Title/Status/Created/Updated. Page sizes 10/25/50, default 25, sorted by Updated desc. | E2, E5 |
| OQ-5a | What is the lookup for in the workflow (at intake? linking a veteran to a case?)? | 4.2/4.6 | Optional lookup during intake. Snapshot of name/address saved on the case. | E2, E4, E5 |
| OQ-5b | May the SSN be stored or displayed, even synthetic? | 4.1/4.6 | Store last 4 only, never log it, mask it in the UI. | E2, E4, E5 |
| OQ-5c | The OpenAPI defines only 200/404. What should happen on a malformed or missing SSN, 5xx or timeout? Is there an SSN format rule? | 4.6 | Mock returns 400 for a missing/empty `ssn`. Client accepts exactly nine ASCII digits or `NNN-NN-NNNN`, and normalizes before POST. Invalid input maps to InvalidInput; denied access to Forbidden; transport/server/protocol failures to Unavailable. Intake may continue without lookup. | E4, E5 |
| OQ-6 | Attachment limits: max size, allowed types, count per case, storage? | 4.2 | 10 MiB (10,485,760 bytes), pdf/png/jpeg/txt/docx, no count limit, filesystem volume. Size/type limits configurable; no count limit in the required scope. | E4, E5 |
| OQ-7 | Which accessibility standard applies (WCAG 2.1 AA, 2.2 AA, Section 508)? | 4.2 | WCAG 2.2 AA as the target; axe, browser tests and manual keyboard/screen-reader review provide evidence, not a conformance claim. | E5 |
| OQ-8 | Which steps make up the "primary workflow"? | 4.2 | A11 (sign in → list → intake → detail/edit → transition → attachments). | E5 |
| OQ-9 | Edit rules: which fields can be edited, in which statuses, by which roles? Are Closed cases read-only? | 4.2/4.3 | Title/Description editable by both roles in any status except Closed. | E2, E5 |
| OQ-10 | Is the initial status of a new case always `New`? | 4.3 | Yes. | E2 |
| OQ-11 | Are all unlisted transitions (including the backward ones, and Closed → anything) forbidden? Is there any reopen from Closed? | 4.3 | All unlisted transitions are forbidden. No reopen from Closed. | E2 |
| OQ-12 | Title length: count after trimming whitespace? Count Unicode characters (text elements) or UTF-16 code units? | 4.4 | Trim Title, then count Unicode scalar values with `EnumerateRunes` for both text limits. PostgreSQL `char_length` agrees for valid supported Unicode. Reject malformed UTF-16 and NUL. See E2-S2. | E2, E5 |
| OQ-13 | How much telemetry is expected ("such as logs")? | 4.2 | Structured logs required. Traces/metrics and the dashboard optional (compose profile). | E1 |
| OQ-14 | Is any database engine acceptable? | 4.5 | PostgreSQL 17 (ADR, with SQL Server as the alternative considered). | E1 |
| OQ-15 | What format and location should the technical decision documentation use? | 4.2 | ADRs in `docs/adr` + `assumptions-and-limitations.md` + README. | E1 |
| OQ-16 | Must "complete automated test suite" include browser E2E in the `tests` container, and is there a runtime limit? | 4.5 | Yes, everything runs in `tests` (no evaluator runtime limit is specified). | E1, E5 |
| OQ-17 | Are delete operations (cases, attachments) expected? | 4.2 | Not implemented (not required). | E2, E4 |
| OQ-18 | Is local username/password auth with demo credentials documented in the README acceptable, with no self-registration? | 4.1/4.7 | Yes. Seeded users only. | E3 |
| OQ-19 | Display time zone: show UTC or the user's local time? | 4.5 | Store and display UTC with an explicit "UTC" label. | E5 |
| OQ-20 | Will evaluation run on ARM hosts (e.g. Apple Silicon) where linux/amd64 needs emulation? | 4.5 | Pin `platform: linux/amd64` as required. Note the emulation impact in the README. | E1 |

</details>

---

<details>
<summary><span style="color: cyan"><b>9. Deliverable acceptance and review evidence</b></span></summary>

### 9.1 Required repository and demonstration outputs

| Deliverable | Owner | Acceptance evidence |
|---|---|---|
| Complete functional application | All; E5 integration | G2/G3 demonstrate every capability in 4.2 using actual persisted data and local HTTP lookup |
| Source and reproducible build | E1 | Clean checkout, no host SDK/secret/environment file needed, Linux amd64 Compose build succeeds |
| One-command startup | E1 with seed owners | Exact `docker compose up --build` builds, migrates, seeds and serves http://localhost:8080; repeat startup preserves edits and attachments |
| Complete one-command suite | E1/E5 with all test owners | Exact `docker compose run --rm tests` discovers every suite and propagates failures, with no runtime restore or download |
| Synthetic data and demo accounts | E2/E3/E4 | Cases across all statuses, two role accounts, known-found/absent lookup examples; no Government data/services/credentials |
| Local lookup implementation and contract | E4 | Supplied OpenAPI preserved, real POST integration verified for 200/404 and documented failure handling |
| Durable attachments | E4 | Upload/download hashes match; container recreation retains content; cross-case requests fail |
| UTC timestamps and audit evidence | E1/E2 | Persisted model including Identity timestamps checked; business changes and audit commit together |
| Operational logs | All; E1 conventions | Safe stdout events cover key operations and failures; captured logs contain no sensitive payloads |
| Technical decisions and exact versions | E1 with all owners | README, docs/versions.md, ADR index/records, assumptions/limitations, requirement traceability and verification evidence |
| Keyboard-accessible primary workflow | E5/E3 | Automated focus/validation/status tests plus recorded manual keyboard and screen-reader checks |
| Demonstration walkthrough | E5 with feature owners | docs/demo.md names accounts and synthetic inputs and covers list controls, validation/audit, edit, all five transitions, role difference, lookup, upload/download and operational logs; no scripted substitute for working behavior |
| Final repository including Government change | E1 plus affected owners | docs/requirement-change.md maps supplied change to code/tests; original suite and new acceptance pass at G4 |

The specification does not state a final repository submission URL, branch, archive format, delivery channel, or deadline. Do not invent them. Record separately when supplied. E1 identifies the exact reviewed commit and confirms source, migrations, lock/pin files, local assets and instructions are committed; omit credentials for real systems, generated build output and evaluator data.

### 9.2 Demonstration administration outside coding stories

The workspace demonstration letter is a separate source of attendance and meeting requirements. A human submission owner maintains the attendee list, confirms no more than five technical contributors, submits the list through the required channel, obtains the Microsoft Teams invitation, and checks meeting/screen-sharing setup. Its original schedule and time-zone wording remain in the letter, outside this implementation plan. No calendar estimates or submission messages are generated by this plan. Sending external correspondence requires explicit user authorization.

### 9.3 Review outcome and technical sources

All explicit requirements from specification sections 4.1–4.7 map to section 7 and to deliverables above. This establishes plan coverage, not implemented compliance. Exact patch pins and image/package compatibility are resolved and proven in E1-S1/G0. G3 proves the current specification; G4 additionally depends on the still-unknown Government change.

The review corrected context lifetime and identity boundaries, the project dependency graph, audit transaction ownership, stale browser concurrency tokens, shared Unicode validation, test discovery/failure checks, build-time browser provisioning and Compose profile/image behavior. Schedule/effort assumptions were removed for agent-assisted execution.

Implementation references checked during review:

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) supports the selected .NET 10 LTS line; resolve exact maintained patches at G0.
- [Blazor and EF Core](https://learn.microsoft.com/en-us/aspnet/core/blazor/blazor-ef-core?view=aspnetcore-10.0) supports factory-created contexts for independent operations.
- [VSTest command](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-vstest) documents the selected runner, no-build/no-restore switches and failure exit behavior.
- [Compose profiles](https://docs.docker.com/compose/how-tos/profiles/) documents selective startup and explicit service targeting. Sharing a built image with web avoids leaving the profiled test image unbuilt.
- [Playwright .NET containers](https://playwright.dev/dotnet/docs/docker) documents included browser dependencies and matching package/image versions.
- [Npgsql concurrency](https://www.npgsql.org/efcore/modeling/concurrency.html) documents uint/xmin mapping; service checks must also compare the submitted browser version.
- [WebApplicationFactory UseKestrel](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.mvc.testing.webapplicationfactory-1.usekestrel?view=aspnetcore-10.0) supports the real-socket browser host.

### 9.4 Plan verification record

The planning review checked 39 unique story IDs, each with Story, Acceptance criteria, Technical notes, Testing and Traces to fields. It checked all 44 traceability entries against the supplied specification, verified explicit story references resolve, checked balanced Markdown code fences, and confirmed obsolete audit contracts and timeline estimates were removed. Requirement entries include permissions and out-of-scope statements as well as mandatory behavior; the count is a traceability inventory, not a count of separate Government obligations.

Application build, container startup, test execution and browser/accessibility verification have not run because implementation has not started. Their required evidence is assigned to G0–G4. Plan approval authorizes starting the foundation; it does not certify a completed application or the unknown Government change.

</details>

---
