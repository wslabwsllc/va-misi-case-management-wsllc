# Change request 2: Veteran Service History, implementation plan

**Source:** Government Common Requirement Change, "Additional Data Source" (section 1 below)
**Review status:** Reviewed against the requirement text and the current code (`main` at a43e242). Every requirement has story ownership and verification criteria. Implementation has not started. Begin with gate H0.
**Target stack:** unchanged from `docs/Epic_Plan.md` (.NET 10, Blazor Interactive Server, EF Core 10, PostgreSQL, Docker Compose v2, Linux x86-64)
**Execution model:** five engineers, each with AI agents, keep the epic ownership areas of `docs/Epic_Plan.md` (E1 to E5). One shared contract PR (H0) comes first. After it, all five lanes work in parallel against fakes, then integrate at H2. Gates express dependencies, not dates or effort.

---

<details>
<summary><span style="color: cyan"><b>0. Introduction</b></span></summary>

### 0.1 Requirement as received

> **Note:** the text is verbatim except for one value. The Government's example SSN uses an issued area number, and
> `SyntheticSsnGuardTests` rejects any such value in the repository. It is shown below as `NNN-NN-NNNN` (area 123 in the original).

COMMON REQUIREMENT CHANGE

**1. Additional Data Source**

All existing Technical Build Specification requirements remain in effect unless expressly changed below.

A new read-only Veteran Service History Service provides the following information based on the same synthetic SSN used by the existing Veteran Lookup Service:

- Military service branch;
- Service start and end dates;
- Character of service; and
- Date the source record was last verified.

**Service Interface**

| | |
|---|---|
| Method / Path | `POST /veterans/service-history/lookup` |
| Request | `{ "ssn": "NNN-NN-NNNN" }` |
| 200 - Found | `{ "branch": "Army", "serviceStartDate": "2001-02-15", "serviceEndDate": "2005-02-14", "characterOfService": "Honorable" }` |
| 404 | No service-history record found |
| 503 | Service temporarily unavailable |

The existing Veteran Lookup Service shall remain the authoritative source for the Veteran's name and address. The new service shall be the authoritative source for service-history information.

**2.0 Required Change**

When the existing Veteran Lookup Service successfully identifies a Veteran, the application shall also attempt to obtain service-history information from the new service using the same SSN.

Returned service-history information shall be associated with the case and available from the case detail.

The existing Veteran Lookup Service remains the source for Veteran name and address. The new service is the source for service-history information.

A no-record response or temporary unavailability of the new service shall not prevent case creation or case processing. The application shall communicate the result to the user.

The Offeror shall include a local implementation, mock, or deterministic stub of the new service using synthetic data so the complete solution can be evaluated without an external dependency.

The Government is specifying the required outcome, not a prescribed implementation. The Offeror may determine the architecture, data model, user-interface treatment, failure-handling approach, and test strategy consistent with the existing Technical Build Specification. Where this change is silent, the Offeror may make reasonable assumption

### 0.2 Requirements

| Req ID | Requirement summary | Stories |
|---|---|---|
| R-CR2-1 | After a successful Veteran Lookup, attempt a service-history lookup with the same SSN | E4-C2-3, E5-C2-1 |
| R-CR2-2 | Service history is associated with the case | E2-C2-1, E2-C2-2, E2-C2-3 |
| R-CR2-3 | Service history is available from the case detail | E2-C2-3, E5-C2-2 |
| R-CR2-4 | Lookup stays the source for name and address; the new service is the source for service history | E2-C2-1, E4-C2-3 |
| R-CR2-5 | A no-record response or an outage never prevents case creation or case processing | E1-C2-2, E5-C2-1, E5-C2-3 |
| R-CR2-6 | The application communicates the service-history result to the user | E5-C2-1, E5-C2-2 |
| R-CR2-7 | A local, deterministic mock with synthetic data; no external dependency | E4-C2-1, E1-C2-2 |
| R-CR2-8 | All original requirements still hold (full regression) | E1-C2-3, E3-C2-2 |

### 0.3 Decisions

Each decision is recorded in ADR 0018 (E4-C2-4) and in `docs/assumptions-and-limitations.md` (CR2-1 to CR2-6).

| # | Decision | Why |
|---|---|---|
| D1 | **Separate service:** new project `CaseIntake.VeteranHistory.Mock`, own container `veteran-history` on port 8082. | The Government describes a separate service. Its failure must be independent, so `docker compose stop veteran-history` gives a real outage demo. The Lookup mock and its pinned Government contract stay untouched. |
| D2 | **History is fetched only at lookup time**, inside the New case page, right after the Lookup returns `Found`. | The SSN is never saved (A8). Once the case exists, nothing can query the service again. A user who sees "unavailable" can look up again before submitting. |
| D3 | **The case stores the outcome, not just the data:** nullable `ServiceHistoryStatus` (`Found`, `NotFound`, `Unavailable`). Null means the case predates this change. | "The application shall communicate the result": the detail page must tell "no record", "service was down" and "not requested" apart. |
| D4 | **Flat nullable columns on `Cases`**, following the existing `VeteranName` / `VeteranAddress` / `VeteranSsnLast4` pattern. | One snapshot per case, taken at intake. No history-of-history is needed. |
| D5 | **Reuse `Permissions.VeteranLookup`**. No new permission. | C1-3 already guarantees anyone who can create cases holds it. A new permission would change the golden matrix and the Roles page for no gain. |
| D6 | **Reuse `VeteranLookupOutcome`** for the client result. Any outcome other than `Found` or `NotFound` is stored as `Unavailable`. | It already has Found, NotFound, InvalidInput, Unavailable and Forbidden. InvalidInput and Forbidden cannot follow a successful lookup (same SSN; C1-3 grants `veteran.lookup` to every case creator), so they need no state of their own. |
| D7 | **`lastVerifiedDate` is an optional response field; `serviceEndDate` is optional too.** | The requirement lists "date last verified", but the sample 200 omits it. An open end date covers someone still serving. |
| D8 | **No readiness health check for the new service; `web` depends on it with `service_started`, not `service_healthy`.** | If it were a readiness dependency, an outage would mark the web app unhealthy or stop it from starting. That breaks "shall not prevent case processing". |
| D9 | **Branch and character of service are free text** (length-capped), not enums. | The new service is the authoritative source. The app must not reject values it does not know. |

### 0.4 Demo data (synthetic, area 900-999)

| SSN | Lookup | Service history |
|---|---|---|
| `900-12-3456` | Found: Rowan Calloway | **Found:** Army, 2001-02-15 to 2005-02-14, Honorable (the Government example), last verified set |
| `902-17-3062` | Found: Harper Kestrel | **404:** no record |
| `962-91-6025` | Found: Casey Juniper | **503:** deterministic outage (constant in the mock) |
| `999-99-9999` | 404 | 404 |
| any other veteran in `veterans.json` | Found | Found (generated) |

### 0.5 Shared contract (gate H0)

E1 merges one foundation PR, written with E2 (Domain) and E4 (Contracts). It contains the types, fake, configuration key and empty mock shell below, and no behavior. Every lane then builds against it. After merge, a change to these types needs approval from the consuming owners, and changes are additive only.

```csharp
namespace CaseIntake.Domain.Cases;

public enum ServiceHistoryStatus { Found, NotFound, Unavailable }
public sealed record ServiceHistory(string Branch, DateOnly ServiceStartDate, DateOnly? ServiceEndDate,
    string CharacterOfService, DateOnly? LastVerifiedOn);
// Two optional trailing parameters, so existing callers compile unchanged.
public sealed record CaseVeteran(string Name, string Address, string SsnLast4,
    ServiceHistoryStatus? HistoryStatus = null, ServiceHistory? History = null);
// CaseFieldLimits: ServiceBranchMaxLength = 50, CharacterOfServiceMaxLength = 100
// CaseFieldNames: ServiceBranch, ServiceStartDate, ServiceEndDate, CharacterOfService (E5 binds field errors to these)
```

```csharp
namespace CaseIntake.Contracts.VeteranLookup;

public sealed record ServiceHistoryResult(VeteranLookupOutcome Outcome, ServiceHistory? History);
public interface IVeteranServiceHistoryClient
{
    Task<ServiceHistoryResult> LookupAsync(string ssn, CancellationToken ct);
}
```

```csharp
namespace CaseIntake.Contracts.Cases;

public sealed record VeteranSnapshot(string Name, string Address, string SsnLast4,
    ServiceHistoryStatus? HistoryStatus = null, ServiceHistory? History = null);
```

The same PR also adds:
- `tests/CaseIntake.TestKit/Fakes/FakeVeteranServiceHistoryClient.cs`, a copy of `FakeVeteranLookupClient` whose default answer is `NotFound`, registered in `FakeModules.cs` next to the lookup fake.
- Configuration key `VeteranHistory:BaseUrl` (plus optional `AttemptTimeout`, `TotalTimeout`, `RetryDelay`, the same as `VeteranLookup`).
- EventIds: E4 client 4010 to 4013, E4 mock 4060, E5 page 5014. All are unused today.

### 0.6 Ownership and shared files

| Engineer | Epic lane | Owns |
|---|---|---|
| Dev 1 | E1 Platform and delivery | H0 PR, `docker-compose.yml`, `CaseIntake.sln`, `CODEOWNERS`, `ProjectGraph.cs`, `VersionConsistencyTests.cs`, scripts, shared docs (README, architecture, versions, testing, troubleshooting, verification, change record, assumptions) |
| Dev 2 | E2 Case domain | `Domain/Cases`, `CaseRequestValidator`, `Persistence/Configurations/Cases`, the migration, `CaseService` |
| Dev 3 | E3 Identity and security | permission and authorization tests, SSN and PII hygiene across new code, `docs/security-scan.md` |
| Dev 4 | E4 Integrations | `CaseIntake.VeteranHistory.Mock`, `CaseIntake.VeteranLookup.Client`, `CaseIntake.VeteranLookup.Tests`, dataset generator, contract YAML, ADR 0018 and the ADR 0008 note |
| Dev 5 | E5 UI and accessibility | `NewCase.razor`, `CaseDetail.razor`, the new `ServiceHistoryDetails.razor`, `Web.Tests`, `E2E`, `docs/demo.md`, `docs/section-508/` |

As in `docs/Epic_Plan.md` 0.8, Contracts, Persistence snapshot, package pins, Compose and `Program.cs` changes go through E1. Each lane updates the docs for its own area and sends shared-doc text to E1.

### 0.7 Gates

| Gate | Required evidence | Accountable owner |
|---|---|---|
| H0 shared contract | The 0.5 PR is merged; the solution builds with zero warnings; every existing test passes unchanged | E1 with E2, E4 |
| H1 lanes complete in isolation | Each story's own tests pass: E2 against PostgreSQL, E4 client against the in-process mock, E5 against the fakes; `docker compose up --build` starts `veteran-history` | Each lane owner |
| H2 integrated slice | Composed stack, real client, real mock: `900-12-3456` is looked up, history is shown, the case is saved and the detail page shows Army / Honorable; the E2E test for this path passes | E1 with E4, E5 |
| H3 change acceptance (`docs/Epic_Plan.md` G4) | `docker compose run --rm tests` passes in full; outage demo passes; every 0.2 requirement has evidence; ADR 0018 accepted; verification entry recorded | E1 with all owners |

| Stories | Need before they start |
|---|---|
| E1-C2-1 | This plan |
| E2-C2-1, E3-C2-1, E4-C2-1, E4-C2-2, E5-C2-1, E5-C2-2 | H0 |
| E1-C2-2 | E4-C2-1 Dockerfile |
| E2-C2-2 | E2-C2-1 |
| E2-C2-3 | E2-C2-1, E2-C2-2 |
| E4-C2-3 | H0 (uses the in-process mock from E4-C2-1 for its round-trip tests) |
| E4-C2-4 | Decisions in 0.3; can start at H0 |
| E3-C2-2 | E4-C2-1, E4-C2-3, E5-C2-1 log events exist |
| E5-C2-3 | H2 |
| E1-C2-3 | All other stories |

### 0.8 Definition of Done

`docs/Epic_Plan.md` 0.5 applies to every story unchanged. In addition:
1. An outage of `veteran-history` never stops the app from starting, staying healthy, or creating and working cases.
2. No SSN, full or partial beyond the stored last four, appears in a log, audit row, database column or exception message.
3. Cases created before the change keep working and show "Not requested".

### 0.9 Review of the earlier single-engineer plan

The decisions and code references in the earlier plan were checked against the code and still hold (types, `VeteranLookupClient.Normalize`, `AuditEvent.Created`, free EventIds, free port 8082, next ADR number 0018). The revision changes the following:

| # | Finding | Change in this plan |
|---|---|---|
| F1 | The work ran as nine serial steps for one person. | One shared contract PR (H0), then five parallel lanes against fakes; only real dependencies stay ordered (0.7). |
| F2 | No requirement IDs, so evidence could not be traced. | R-CR2-1 to R-CR2-8 (0.2), each mapped to stories and tests. |
| F3 | Accessibility evidence was missing: the new UI states were not in the Section 508 documents. | E5-C2-3 adds them to `docs/section-508/accessibility-test-matrix.md` and runs axe on each new state. |
| F4 | The migration name `CR2_VeteranServiceHistory` broke the `YYYYMMDDHHmm_<Prefix>_<Change>` convention. | `<timestamp>_CR2_VeteranServiceHistory` (E2-C2-2). |
| F5 | ADR 0018, the contract YAML and the PII review had no owner. | ADR and contract: E4. PII review: E3. |
| F6 | A lookup can now take about 10 s (two 5 s budgets) with no criterion for the busy state. | E5-C2-1 requires `aria-busy` and an announced busy state for the whole call. |
| F7 | Doc changes were pinned to line numbers, which drift when five people edit at once. | Docs are referenced by file and section only. |
| F8 | Authorization was assumed (D5) but not tested for the new client and the new UI. | E3-C2-1. |

</details>

---

<details>
<summary><span style="color: cyan"><b>EPIC 1: Platform, Compose and delivery (Dev 1)</b></span></summary>

**Goal:** Land the shared contract, add the new container without making it a startup or readiness dependency, and run the final regression and documentation pass.

**Scope:** H0 PR, Compose, solution and architecture rules, version and scan checks, shared docs.
**Out of scope:** domain rules (E2), the mock and client (E4), UI (E5).

### E1-C2-1: Shared contract foundation PR
**Status: ⏳ Not started**
**Story:** As an engineer on any lane, I want the shared types, fake and configuration key merged first, so that all five lanes can build in parallel without waiting for each other.
**Acceptance criteria:**
- Given the H0 PR, when the solution builds, then the types in 0.5 exist exactly once, with zero warnings.
- Given existing callers of `CaseVeteran`, `VeteranSnapshot` and `Builders.Veteran`, when they compile, then none of them changed.
- Given `AddFakeModules()`, when it runs, then `IVeteranServiceHistoryClient` resolves to the fake, which answers `NotFound` by default.
- Given `src/CaseIntake.Web/appsettings.json`, when the app starts locally or in `CaseIntakeWebFactory`, then `VeteranHistory:BaseUrl` is `http://localhost:8082`.
- Given the full suite, when it runs, then every existing test passes.
**Technical notes:** E2 writes the Domain part, E4 the Contracts part, E1 merges. `CaseIntake.VeteranHistory.Mock` is added to the sln as an empty `HistoryMockProgram` shell so `ProjectGraph` and CODEOWNERS land in the same PR. First delete the ignored leftovers of an earlier attempt (`git clean -fdX src/CaseIntake.VeteranHistory.Mock src/CaseIntake.Web/bin`) so no stale build output masks a missing file.
**Testing:** existing suite; a one-line check that the fake is registered.
**Traces to:** R-CR2-2, R-CR2-8

### E1-C2-2: `veteran-history` container and outage-safe topology
**Status: ⏳ Not started**
**Story:** As an evaluator, I want the new service to start with `docker compose up --build` and to be stoppable without harming the app, so that I can see both normal and outage behavior.
**Acceptance criteria:**
- Given a clean clone, when I run `docker compose up --build`, then `veteran-history` starts as `linux/amd64` on port 8082, is not published to the host, and becomes healthy.
- Given `web` and `web-e2e`, when I inspect Compose, then they set `VeteranHistory__BaseUrl: http://veteran-history:8082` and depend on `veteran-history` with `service_started` (D8).
- Given the stack is running, when I run `docker compose stop veteran-history`, then `web` stays healthy, `/health/ready` returns 200, and cases can be created.
- Given `tests`, when it runs, then it sets the same base URL and waits for `veteran-history` with `service_healthy`.
- Given the new Dockerfile, when `VersionConsistencyTests` runs, then its SDK and runtime digests are checked like the other Dockerfiles.
- Given `ProjectGraph`, when the architecture test runs, then `VeteranHistory.Mock` has the same rules as `VeteranLookup.Mock`, and no feature module references either mock.
**Technical notes:** the Dockerfile comes from E4-C2-1. `scripts/scan-images.sh` and `scripts/run-offline-check.sh` read services from Compose, so only their header comments change. No change to `ServiceDefaults` health checks.
**Testing:** `VersionConsistencyTests`, `ProjectGraph`. A stopped container is covered by the client's transport-error test (E4-C2-3) and a manual `docker compose stop` run recorded in `docs/verification.md`; the E2E 503 path (E5-C2-3) covers the UI.
**Traces to:** R-CR2-5, R-CR2-7

### E1-C2-3: Regression, shared documents and change record
**Status: ⏳ Not started**
**Story:** As the offeror, I want the change recorded and the full suite green, so that the final submission meets both the original specification and this change.
**Acceptance criteria:**
- Given the final branch, when `docker compose run --rm tests` runs, then every project passes, including the SSN guard, architecture rules and version consistency.
- Given `docs/change-implementation-record.md`, when I read it, then it has a "Change 2: Veteran Service History" section with the same fields as Change 1.
- Given `docs/assumptions-and-limitations.md`, when I read it, then A12 names both mocks as the separate services, CR2-1 to CR2-6 record D7, D2, D1 with D8, D5, D3 and D9, and the limitation "an `Unavailable` result is permanent for that case" is listed.
- Given `docs/architecture.md` and `docs/architecture-overview.md`, when I read them, then the diagrams, module rules, project lists, mock section and startup order show `veteran-history` and that `web` waits only for it to start.
- Given `README.md`, `docs/testing.md`, `docs/docker-troubleshooting.md` and `docs/versions.md`, when I read them, then they list the new service, local run command, project, demo SSNs (0.4), test coverage and Dockerfile re-pin step.
- Given `docs/verification.md`, when H3 is recorded, then it holds a dated Change 2 entry with revision, commands, exit codes and test counts.
**Technical notes:** each lane sends the text for its own area; E1 merges. Verification and security scan entries come last because they record the final run.
**Testing:** the full suite; existing doc-consistency tests.
**Traces to:** R-CR2-8

</details>

---

<details>
<summary><span style="color: cyan"><b>EPIC 2: Case domain, persistence and service (Dev 2)</b></span></summary>

**Goal:** Store the service-history outcome with the case, enforce it in the domain and the database, and return it from the case service.

**Scope:** `Domain/Cases`, `Contracts/Cases/CaseRequestValidator.cs`, `Persistence/Configurations/Cases`, the migration, `CaseService`.
**Out of scope:** fetching history (E4, E5), display (E5).

### E2-C2-1: Service-history domain rules
**Status: ✅ Done**
**Story:** As a system, I want the case to accept only a consistent service-history snapshot, so that bad data from any caller is rejected before it is saved.
**Acceptance criteria:**
- Given status `Found`, when a case is created, then a history is required; given any other status or null, then a history is rejected.
- Given a history, when branch or character of service is blank, or over 50 or 100 characters, then a field error is returned (reuse `RequiredText`).
- Given a history, when the start date is after the end date, then it is rejected; equal dates and a missing end date are accepted.
- Given the same request, when `Case.Create` and `CaseRequestValidator` validate it, then both apply `CaseTextRules.ValidateServiceHistory` and give the same result.
- Given a history status without a veteran, when a case is created, then it is rejected.
**Technical notes:** `Case` gets six private-set properties: `ServiceHistoryStatus`, `ServiceBranch`, `ServiceStartDate`, `ServiceEndDate`, `CharacterOfService`, `ServiceRecordVerifiedOn`. `Veteran` builds them into `CaseVeteran`; `Create` sets them. No "last verified not after today" rule: domain rules take no clock, and the service is authoritative (D9).
**Testing:** `CaseTextRules` tests in `Cases.Tests`: start = end, start > end, caps at 50/51 and 100/101, history with a non-`Found` status, status without a veteran.
**Traces to:** R-CR2-2, R-CR2-4

### E2-C2-2: Persistence mapping and migration
**Status: ✅ Done**
**Story:** As a system, I want the database to enforce the same service-history rules, so that no inconsistent row can be stored.
**Acceptance criteria:**
- Given the migration, when it is applied to a database with the 72 seeded cases, then six nullable columns are added and existing rows are unchanged.
- Given a row, when the status is outside the enum, set without a veteran, `Found` without branch, start and character (or not `Found` with any of them), or has an end date before the start date, then the matching check constraint rejects it: `CK_Case_ServiceHistory_Range`, `_RequiresVeteran`, `_Complete`, `_Dates`.
- Given the model, when `HasPendingModelChanges()` runs, then it is false.
**Technical notes:** status mapped with `HasConversion<int>()`, like `Status`. Migration `<YYYYMMDDHHmm>_CR2_VeteranServiceHistory`, regenerated on rebase if the snapshot conflicts; E1 reviews it.
**Testing:** integration tests in `IntegrationTests/Cases`, one per constraint, plus the existing pending-changes test.
**Traces to:** R-CR2-2

### E2-C2-3: Create and read service history through `ICaseService`
**Status: ✅ Done**
**Story:** As a case worker, I want the service history captured at intake to be saved with the case and returned with its detail, so that it is available later.
**Acceptance criteria:**
- Given a `CreateCaseRequest` with each of `Found`, `NotFound` and `Unavailable`, when the case is created and read back, then `CaseDetailDto.Veteran` carries the same status and history.
- Given a seeded case, when it is read, then the status is null.
- Given a created case, when I read its audit trail, then the Created entry's details read like `Veteran snapshot recorded; service history: NotFound`: the status only, no history values (C2-4).
- Given an invalid snapshot, when the case is created, then a field-specific `Validation` error is returned and nothing is saved.
**Technical notes:** `CreateAsync` maps the snapshot into `CaseVeteran` and passes the status to `AuditEvent.Created(occurredAtUtc, hasVeteranSnapshot, ServiceHistoryStatus? serviceHistory = null)`; `ToDetail` maps back. Seed and existing test callers of `AuditEvent.Created` do not change. `CaseLog.CaseCreated` is unchanged.
**Testing:** integration tests for the three states, the null case, audit details and rejection.
**Traces to:** R-CR2-2, R-CR2-3

</details>

---

<details>
<summary><span style="color: cyan"><b>EPIC 3: Authorization and data protection (Dev 3)</b></span></summary>

**Goal:** Prove the new path uses the existing permission correctly and leaks no SSN anywhere.

**Scope:** authorization tests, log and storage hygiene tests across the new code, the security scan record.
**Out of scope:** new permissions (D5), feature code.

### E3-C2-1: Authorization for service history
**Status: ⏳ Not started**
**Story:** As a security reviewer, I want the service-history lookup governed by the existing `veteran.lookup` permission, so that the change adds no new access path.
**Acceptance criteria:**
- Given a user without `veteran.lookup`, when the history client is called, then it returns `Forbidden`, makes no network request and logs `AuthorizationDenied`.
- Given the golden role-permission matrix, when it is tested, then it is unchanged and the Roles page lists no new permission.
- Given a user who can view a case but not create one, when they open the case detail, then they see the stored history (it is case data, governed by `cases.view`).
**Technical notes:** permission check order in the client is permission, then SSN format, then the call (E4-C2-3). D5 relies on C1-3: anyone who can create cases holds `veteran.lookup`.
**Testing:** `VeteranServiceHistoryClientTests` permission case (with E4), golden matrix test unchanged, bUnit detail test per role (with E5).
**Traces to:** R-CR2-1, R-CR2-3

### E3-C2-2: SSN and PII hygiene for the new data path
**Status: ⏳ Not started**
**Story:** As a security reviewer, I want automated proof that no SSN reaches logs, audit or storage on the new path, so that the original PII rule still holds.
**Acceptance criteria:**
- Given every new log event (4010 to 4013, 4060, 5014), when it is captured on success and failure paths, then it contains no SSN and no veteran name, address or history values; only outcome and duration.
- Given a created case, when I inspect the database and audit rows, then no full SSN is stored.
- Given the new `service-history.json` and contract YAML, when `SyntheticSsnGuardTests` runs, then every SSN is in area 900-999.
- Given `scripts/scan-images.sh`, when it runs, then `docs/security-scan.md` has a `caseintake-veteran-history` row.
**Technical notes:** extend the existing SSN-free captured-log test rather than adding a new mechanism.
**Testing:** captured-log tests in `VeteranLookup.Tests` and `Web.Tests`; integration check of stored columns.
**Traces to:** R-CR2-8

</details>

---

<details>
<summary><span style="color: cyan"><b>EPIC 4: Service History mock and client (Dev 4)</b></span></summary>

**Goal:** Deliver the local Service History service and a resilient client for it, sharing the Lookup client's transport without changing its behavior.

**Scope:** `CaseIntake.VeteranHistory.Mock`, `CaseIntake.VeteranLookup.Client`, `CaseIntake.VeteranLookup.Tests`, the dataset generator, the contract YAML, ADR 0018.
**Out of scope:** UI (E5), storage (E2).

### E4-C2-1: Service History mock service
**Status: ⏳ Not started**
**Story:** As an evaluator, I want a local, deterministic Service History service with synthetic data, so that the change can be evaluated without an external dependency.
**Acceptance criteria:**
- Given `900-12-3456`, when `POST /veterans/service-history/lookup` is called, then it returns 200 `{ branch, serviceStartDate, serviceEndDate, characterOfService, lastVerifiedDate }` matching the Government example; dashed and nine-digit forms give the same result.
- Given `902-17-3062` or an SSN not in the dataset, then 404; given `962-91-6025`, then 503; given a blank or missing `ssn`, then 400 problem+json.
- Given `GET /health`, then 200.
- Given `service-history.json`, when the drift test runs, then it matches the generator, every SSN is in area 900-999 and exists in `veterans.json`, and `veterans.json` is byte-identical to before.
- Given any request, when it is logged, then only outcome and duration are written (event 4060 `ServiceHistoryServed`).
**Technical notes:** same shape as `CaseIntake.VeteranLookup.Mock`, cut down: explicit `HistoryMockProgram` (a different name from `MockProgram` so one test project loads both), `CreateSlimBuilder`, `AddServiceDefaults(includeHealthChecks: false)`, `UseOperationContext`, `AddProblemDetails`, JSON source generation. `ServiceHistoryDataset`: embedded JSON in a `FrozenDictionary`, copied from `VeteranDataset` (about 20 lines), no reference to the Lookup mock. Generator in `VeteranDatasetGenerator` with a **separate** `Random` seed, skipping the 404 and 503 SSNs; `scripts/update-contract-snapshots.sh` writes both files. Dockerfile: copy of the Lookup mock's with the same pinned digests, no `docs/contracts` line, `ASPNETCORE_URLS=http://+:8082`, `EXPOSE 8082`.
**Testing:** `HistoryMockServiceTests` in `VeteranLookup.Tests` (project reference added; no new test project, so `run-tests.sh` is unchanged), in process and against the container through a second `TestSupport` target reading `VeteranHistory__BaseUrl`; drift test.
**Traces to:** R-CR2-7

### E4-C2-2: Service History contract document
**Status: ⏳ Not started**
**Story:** As a reviewer, I want a written contract for the new service, so that the mock and client can be checked against something other than each other.
**Acceptance criteria:**
- Given `docs/contracts/veteran-service-history.openapi.yaml`, when I read it, then it describes the path, request, 200, 404 and 503 from section 0.1, adds 400 and the optional `lastVerifiedDate` (D7) as marked extensions, and uses a 900-area example SSN.
- Given the mock, when `HistoryMockServiceTests` run, then every documented response is exercised.
**Technical notes:** the Government supplied no OpenAPI for this service, so this is the team's own document. No conformance machinery like the Lookup's pinned-hash test: that exists for a Government-supplied file.
**Testing:** covered by E4-C2-1 tests.
**Traces to:** R-CR2-7

### E4-C2-3: Resilient Service History client with shared transport
**Status: ⏳ Not started**
**Story:** As a case worker, I want the app to fetch service history and handle every outcome gracefully, so that intake keeps working whatever the service does.
**Acceptance criteria:**
- Given 200 with a body that passes `ValidateServiceHistory`, when `LookupAsync` is called, then the result is `Found` with the history.
- Given 404, then `NotFound`.
- Given 503, any other status, a timeout, a transport error, malformed JSON, a bad date, a missing branch, or start after end, then `Unavailable`. Caller cancellation propagates.
- Given an SSN that fails `VeteranLookupClient.Normalize`, then `InvalidInput` with no network call.
- Given an invalid `VeteranHistory` base URL or timeout, when the app starts, then start-up fails (`ValidateOnStart`).
- Given the shared transport refactor, when the existing `VeteranLookupClientTests` run, then they pass unchanged.
- Given the real client against the real mock, when each demo SSN in 0.4 is looked up, then the expected outcome is returned.
**Technical notes:** move the budget token, `PostAsJsonAsync` and the catch that returns `Unavailable` out of `VeteranLookupClient.LookupAsync` into one internal helper used by both clients; only the response mapping differs. `VeteranHistoryClientOptions` (section `VeteranHistory`) mirrors the Lookup options. `AddVeteranLookupClient` registers both typed clients through one local function for the resilience pipeline (`veteran-history`). `LookupLog` events 4010 `ServiceHistoryCompleted`, 4011 `ServiceHistoryUnavailable`, 4012 `ServiceHistoryRejected`, 4013 `ServiceHistoryRetrying`.
**Testing:** `VeteranServiceHistoryClientTests` with a stub handler for every row above; extend `ClientAgainstMockTests` for one round trip per demo SSN.
**Traces to:** R-CR2-1, R-CR2-4, R-CR2-5

### E4-C2-4: ADR 0018 and ADR 0008 note
**Status: ⏳ Not started**
**Story:** As an evaluator, I want the design of this change recorded, so that I can judge the alternatives considered.
**Acceptance criteria:**
- Given `docs/adr/0018-veteran-service-history.md` (from `0000-template.md`), when I read it, then it records D1 to D9, the options considered (same vs separate container; fetch at lookup vs later; flat columns vs a separate table) and the consequences (an outage never blocks startup or intake; history is never refreshed).
- Given `docs/adr/0008-veteran-lookup-mock-and-client.md`, then its status says "Extended by ADR 0018 (shared client transport, second mock)"; `docs/adr/README.md` indexes 0018.
**Technical notes:** E2 and E5 review the sections on data model and UI treatment.
**Testing:** existing doc link checks.
**Traces to:** R-CR2-1 to R-CR2-7

</details>

---

<details>
<summary><span style="color: cyan"><b>EPIC 5: Intake and detail UI, end-to-end and accessibility (Dev 5)</b></span></summary>

**Goal:** Show the service-history result at intake and on the case detail, never let it block submission, and prove the whole path end to end and accessibly.

**Scope:** `NewCase.razor`, `CaseDetail.razor`, new `ServiceHistoryDetails.razor`, `Web.Tests`, `E2E`, `docs/demo.md`, `docs/section-508/`.
**Out of scope:** client behavior (E4), storage rules (E2).

### E5-C2-1: Service history at intake
**Status: ⏳ Not started**
**Story:** As a case worker, I want to see the veteran's service history right after the lookup, and to submit the case whatever the result, so that history problems never block my work.
**Acceptance criteria:**
- Given a lookup returns `Found`, when the page continues, then it calls the history client with the same SSN before the SSN is cleared.
- Given history `Found`, then the result card shows branch, service dates, character of service and last verified.
- Given `NotFound`, then an info message reads "No service-history record was found for this veteran. You can still submit the case."
- Given `Unavailable` (or any other outcome, D6), then a warning reads "The service-history service is unavailable right now. You can submit without it, or look up again to retry."
- Given the history client throws, then the found veteran is kept, the result is `Unavailable`, and event 5014 `ServiceHistoryUiFailed` is logged.
- Given any of the three results, when I choose "Use this veteran" and submit, then the case is created and the request carries that status and history.
- Given both calls are running (up to about 10 s), then the button stays "Look up veteran", the form is `aria-busy`, and the busy state and result are announced in the existing `role="status"` region.
- Given a selected veteran, then the selected-veteran card shows the history status in one line.
**Technical notes:** inject `IVeteranServiceHistoryClient`; call it in `LookUpAsync`'s `Found` branch inside its own try/catch. `_found` holds status and history; `UseVeteranAsync` carries them into `_selected`, which goes into `CreateCaseRequest` unchanged. Reuse the existing `message` styles.
**Testing:** bUnit in `NewCaseTests` with the fake: each outcome, the exception path, and "outage does not block intake" (lookup `Found`, history `Unavailable`, warning shown, submit succeeds with `Unavailable`). `WebTestContext` exposes and registers `History` next to `Lookup`.
**Traces to:** R-CR2-1, R-CR2-5, R-CR2-6

### E5-C2-2: Service history on the case detail
**Status: ⏳ Not started**
**Story:** As a case worker, I want the case detail to show the service history captured at intake, or why there is none, so that I know what was found.
**Acceptance criteria:**
- Given the Veteran card, when the case renders, then an `h3` "Service history" is followed by a `dl` or one sentence:

  | Status | Shown |
  |---|---|
  | `Found` | the fields; a missing end date shows "Present"; a missing last-verified date shows "Not provided" |
  | `NotFound` | "No service-history record was found at intake." |
  | `Unavailable` | "The service-history service was unavailable when this case was opened." |
  | null, with a veteran | "Not requested for this case." (Neutral: a null from a caller that omits the status must not claim the case predates the change.) |
- Given dates, then they use `UiText.Date`.
- Given the New case page and the detail page, then both render history through the same `ServiceHistoryDetails.razor`.
**Technical notes:** data comes from `CaseDetailDto.Veteran` (E2-C2-3); no extra service call.
**Testing:** bUnit for all four states and the missing end/verified dates.
**Traces to:** R-CR2-3, R-CR2-6

### E5-C2-3: End-to-end, outage and accessibility evidence
**Status: ⏳ Not started**
**Story:** As an evaluator, I want automated proof that the change works through the real stack, including during an outage, and stays accessible, so that I can trust it without manual setup.
**Acceptance criteria:**
- Given the E2E suite, when it runs, then it looks up `900-12-3456`, sees the history, submits, and the detail shows Army / Honorable; then `962-91-6025` shows the warning and the case is still created.
- Given each new state at intake and on the detail page, when axe runs, then there are zero WCAG 2.x A/AA violations.
- Given `docs/section-508/accessibility-test-matrix.md` and `manual-test-procedures.md`, then they list the new states with keyboard and screen-reader checks for the announcements.
- Given `docs/demo.md`, then it walks through Found, NotFound and Unavailable, and the `docker compose stop veteran-history` outage.
**Technical notes:** extend `PrimaryWorkflowTests`; no fixed sleeps; Playwright auto-wait.
**Testing:** this story is the test.
**Traces to:** R-CR2-1, R-CR2-3, R-CR2-5, R-CR2-6

</details>

---

<details>
<summary><span style="color: cyan"><b>6. Story summary</b></span></summary>

| Epic | Stories |
|---|---|
| E1 Platform, Compose and delivery | 3 |
| E2 Case domain, persistence and service | 3 |
| E3 Authorization and data protection | 2 |
| E4 Service History mock and client | 4 |
| E5 Intake and detail UI, E2E and accessibility | 3 |
| Total | 15 |

E3 has the lightest load. After E3-C2-1, Dev 3 pairs with Dev 4 on `VeteranServiceHistoryClientTests` and reviews every PR for PII before H2.

</details>

---

<details>
<summary><span style="color: cyan"><b>7. Done when (gate H3)</b></span></summary>

- [ ] `docker compose up --build` starts `veteran-history`; `900-12-3456` shows and saves the Army / Honorable history.
- [ ] `902-17-3062` shows "no record"; `962-91-6025` shows "unavailable"; both cases are created.
- [ ] With `docker compose stop veteran-history`, the app stays healthy, lookups still find veterans, and cases are created as `Unavailable`.
- [ ] Seeded cases show "Not requested".
- [ ] No SSN appears in logs, audit rows or the database.
- [ ] `docker compose run --rm tests` passes, including the SSN guard, the architecture rules and version consistency.
- [ ] ADR 0018 is accepted and indexed; the architecture diagrams and startup order show `veteran-history`.
- [ ] Every document named in the stories is updated; `scripts/scan-images.sh` reports the new image; `docs/verification.md` has the dated Change 2 entry.

### Deliberately not done

- No refreshing of history after the case exists (needs the SSN, which is never stored).
- No new permission and no readiness health check for the new service.
- No enum lists for branch or character of service.
- No separate test project.
- `docs/Epic_Plan.md` is a historical delivery plan and stays as is.

</details>
