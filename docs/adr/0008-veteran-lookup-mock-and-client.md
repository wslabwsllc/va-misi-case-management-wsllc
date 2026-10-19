# 0008. Veteran Lookup: code-first mock, generated OpenAPI contract and a resilient typed client

* Status: accepted
* Owner: E4

## Context

Spec 4.2/4.6 require a working connection to a sample data service with `POST /veterans/lookup` (required `ssn`;
200 with `name` and `address`; 404 when unknown), allow a local deterministic mock (4.1), and forbid Government
services or real data (4.1, 4.7). The app must run with no runtime dependency on external hosts (4.5). The spec does
not say what happens on malformed input, 5xx or timeouts, or whether an SSN has a format rule (OQ-5c). The
committed contract file is ours to author (tech lead decision, 2026-10-17); no definition was handed to the team.

## Decision

**Mock service** (`src/CaseIntake.VeteranLookup.Mock`, its own `veteran-lookup` container on port 8081, not
published to the host). An ASP.NET Core minimal API over an embedded, deterministic dataset of 50 synthetic records
(`Data/veterans.json`, generated with a fixed seed by `VeteranDatasetGenerator` in the test project). Every SSN has
area number 900-999, which is never issued; ZIP codes are `000xx`, which are unassigned. `900-12-3456` is the
documented hit and `999-99-9999` the documented miss. It logs only the outcome and duration.

**Contract is code-first.** `Microsoft.AspNetCore.OpenApi` (built into ASP.NET Core, the equivalent of springdoc for
Spring Boot) generates an OpenAPI 3.1 document from the endpoint declarations, served at `/openapi/v1.yaml` and
`/openapi/v1.json`. A document transformer fixes `info` and removes `servers`, so the output does not depend on the
request host. The YAML is committed as `docs/contracts/veteran-lookup.openapi.yaml`; a drift test fails the suite
when the code and the file differ. `scripts/update-contract-snapshots.sh` regenerates it for review. .NET 10 cannot
emit YAML at build time, so the snapshot is produced by a test that hosts the mock in memory.

**Local rules beyond the spec (OQ-5c), documented in the contract:** 400 (problem+json) for an absent or malformed
JSON body or a missing, null, non-string, empty or blank `ssn` (always with a problem+json body, also when the
framework rejects the body before the handler runs); 415 for a non-JSON content type. The SSN format is
checked by the client, not the mock.

**Client** (`src/CaseIntake.VeteranLookup.Client`, references Contracts only). A typed `HttpClient` behind
`IVeteranLookupClient`:

1. checks `veteran.lookup` for the current actor (denied → `Forbidden`, no request; `AuthorizationDenied` logged);
2. accepts exactly nine ASCII digits or `NNN-NN-NNNN` (otherwise `InvalidInput`, no request) and sends the dashed form;
3. maps 200 with a usable body to `Found`, 404 to `NotFound`, and everything else (other statuses, malformed or
   oversized bodies, connection errors, timeouts) to `Unavailable`, so intake can continue without the lookup;
4. lets the caller's own cancellation propagate.

The resilience pipeline is one retry (connection errors, timeouts, 408, 429, 5xx; constant 200 ms delay;
`Retry-After` ignored to keep the budget) around a per-attempt timeout of 2 s. The 5 s total budget is a
cancellation token owned by the client rather than a pipeline timeout, because the pipeline ends once response
headers arrive and the budget must also cover reading the body. Retrying is safe because the lookup is read-only.
The base URL must be absolute `http`/`https` (checked at startup). Polly's own telemetry is turned off for this
pipeline because it logs raw exception messages; the client logs its own events (4001 completed, 4002 unavailable,
4003 rejected, 4004 retrying) with safe fields only.
Readiness (`/health/ready`) probes the mock's `/health` (ServiceDefaults).

## Alternatives considered

- **Hand-written, spec-first YAML.** The usual choice when a provider publishes a contract. Here no definition was
  supplied and the mock is ours, so a hand-written file could drift silently from the running service.
- **Swashbuckle or NSwag.** Mature generators with a Swagger UI, but third-party dependencies duplicating what the
  framework now ships. A UI adds nothing because the mock is not published to the host (decision D10).
- **WireMock-style stub or an in-process fake.** Less code, but not a real HTTP service in its own container, which
  4.6 and the integration evidence (R-4.2-16) call for.
- **`AddStandardResilienceHandler`.** Includes a circuit breaker and hedging tuned for high-volume services and
  makes the five-second budget harder to reason about. A small explicit pipeline is easier to explain and test.

## Tradeoffs

- The contract describes what the mock does, not an externally imposed definition. If VA later supplies a fixed
  definition, the mock changes to match and the drift test makes the change visible in review.
- The 400/415 behaviour and the SSN format rule are local decisions; a different real service may differ.
- Up to five seconds of waiting when the service is down, in exchange for riding out a single transient failure.

## Consequences

- Changing the mock's endpoints requires regenerating and committing the snapshot (the suite fails otherwise).
- The lookup tests run against both the in-process mock and, inside `docker compose run --rm tests`, the real
  `veteran-lookup` container.
- No SSN, SSN hash, name or address appears in any log; this is asserted on success and failure paths.
