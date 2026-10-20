# 0008. Veteran Lookup: mock serving the Government contract verbatim, conformance-tested implementation and a resilient typed client

* Status: accepted
* Owner: E4
* Amended: 2026-10-20 (US-4.1 / R12). The contract is the Government's definition, not one generated from our code.

## Context

Spec 4.2/4.6 require a working connection to a sample data service with `POST /veterans/lookup` (required `ssn`;
200 with `name` and `address`; 404 when unknown), allow a local deterministic mock (4.1), and forbid Government
services or real data (4.1, 4.7). The app must run with no runtime dependency on external hosts (4.5). The spec does
not say what happens on malformed input, 5xx or timeouts, or whether an SSN has a format rule (OQ-5c).

**Specification 4.6 requires the service to conform to the Government's definition. The mock serves that definition
verbatim as its contract. A test proves the implementation conforms. 400/415 are documented extensions for inputs the
spec doesn't define.** (The 2026-10-17 decision that the contract was ours to author, because no definition had been
supplied, was wrong: 4.6 includes the definition. It is reversed by this amendment.)

## Decision

**Mock service** (`src/CaseIntake.VeteranLookup.Mock`, its own `veteran-lookup` container on port 8081, not
published to the host). An ASP.NET Core minimal API over an embedded, deterministic dataset of 50 synthetic records
(`Data/veterans.json`, generated with a fixed seed by `VeteranDatasetGenerator` in the test project). Every SSN has
area number 900-999, which is never issued; ZIP codes are `000xx`, which are unassigned. `900-12-3456` is the
documented hit and `999-99-9999` the documented miss. It logs only the outcome and duration.

**Contract is spec-first: the Government's file, verbatim.** The OpenAPI 3.0.3 definition from 4.6 is committed
byte for byte as `docs/contracts/veteran-lookup.spec.openapi.yaml` (LF line endings, enforced by `.gitattributes`),
pinned by a SHA-256 test, embedded in the mock and served unchanged at `/openapi/v1.yaml` (`GovernmentContract`). A
test checks the served bytes against the pin, both in process and against the `veteran-lookup` container.

**Conformance is proven, not asserted.** `Microsoft.AspNetCore.OpenApi` (built into ASP.NET Core, the equivalent of
springdoc for Spring Boot) still generates a document from the endpoint declarations, as OpenAPI 3.0 with the
definition's `info` and response descriptions, at `/openapi/implementation.json`. It is evidence only, not a
contract. `OpenApiConformanceTests` parses the Government file (`Microsoft.OpenApi.YamlReader`, tests only), follows
`$ref`s in the generated document, and compares every element: path, method, request body, content types, schemas,
required properties, responses and descriptions. The only differences it accepts are on an explicit allowlist, each
tied to an assumption (D11-D15): 400 and 415, `operationId`/tags/description, `$ref` components, and a stricter
`required` on `name`/`address`. Anything else fails, and a second test fails when an allowlist entry is no longer
needed. Behaviour tests also send requests built only from the definition to the running service and check the 200
and 404 shapes against its schemas, and check the client's wire request against its request schema.

**Local rules beyond the spec (OQ-5c), documented extensions (D11, D12), not part of the contract:** 400
(problem+json) for an absent or malformed JSON body or a missing, null, non-string, empty or blank `ssn` (always with
a problem+json body, also when the framework rejects the body before the handler runs); 415 for a non-JSON content
type. The SSN format is checked by the client, not the mock.

**Client** (`src/CaseIntake.VeteranLookup.Client`, references Contracts only). A typed `HttpClient` behind
`IVeteranLookupClient`:

1. checks `veteran.lookup` for the current actor (denied → `Forbidden`, no request; `AuthorizationDenied` logged);
2. accepts exactly nine ASCII digits or `NNN-NN-NNNN` (otherwise `InvalidInput`, no request) and sends the dashed form;
3. maps 200 with a usable body to `Found`, 404 to `NotFound`, and everything else (other statuses, malformed or
   oversized bodies, connection errors, timeouts) to `Unavailable`, so intake can continue without the lookup. The
   definition leaves `name` and `address` optional; a 200 without both cannot fill the case's veteran snapshot, so it is
   `Unavailable` too (D14);
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

- **Code-first generated contract** (the previous decision, superseded 2026-10-20). A generated document can never
  match the Government text (it adds 400/415, `$ref`, `operationId` and tags, and writes its own version), and the
  claim that no definition was supplied contradicted 4.6. Kept only as conformance evidence.
- **Force the generated document's version string to 3.0.3.** A string rewrite of serializer output; unnecessary
  once the verbatim file is the contract.
- **Hand-coded expectations in C# instead of parsing the YAML.** No new package, but the test would no longer read the
  definition itself.
- **Swashbuckle or NSwag.** Mature generators with a Swagger UI, but third-party dependencies duplicating what the
  framework now ships. A UI adds nothing because the mock is not published to the host (decision D10).
- **WireMock-style stub or an in-process fake.** Less code, but not a real HTTP service in its own container, which
  4.6 and the integration evidence (R-4.2-16) call for.
- **`AddStandardResilienceHandler`.** Includes a circuit breaker and hedging tuned for high-volume services and
  makes the five-second budget harder to reason about. A small explicit pipeline is easier to explain and test.

## Tradeoffs

- The contract is externally imposed and never edited by the team. Our extensions live only in the implementation
  and are listed in D11-D15; a revised definition from the Government replaces the file and its pin.
- One test-only package (`Microsoft.OpenApi.YamlReader`, which brings `SharpYaml`) to parse the definition; it is not
  in any application image.
- The 400/415 behaviour and the SSN format rule are local decisions; a different real service may differ.
- Up to five seconds of waiting when the service is down, in exchange for riding out a single transient failure.

## Consequences

- Any change to the mock outside the allowlist fails the conformance test; the Government file does not change
  without a new specification.
- `/openapi/v1.json` and the generated `/openapi/v1.yaml` of the code-first design no longer exist: `/openapi/v1.yaml`
  now returns the Government file, and the generated document is `/openapi/implementation.json` (evidence only).
- The lookup tests run against both the in-process mock and, inside `docker compose run --rm tests`, the real
  `veteran-lookup` container.
- No SSN, SSN hash, name or address appears in any log; this is asserted on success and failure paths.
