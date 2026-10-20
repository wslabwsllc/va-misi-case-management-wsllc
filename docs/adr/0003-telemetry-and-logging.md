# 0003. Structured JSON logs; no telemetry exporters

* Status: accepted
* Owner: E1

## Context

The spec asks for operational telemetry "such as logs" (OQ-13) and forbids runtime dependence on external services.
Logs must never carry SSNs or secrets.

## Decision

A custom console formatter writes one JSON object per line: UTC timestamp, level, category, EventId, message,
structured state and scopes (operation ID, plus TraceId/SpanId when an activity exists). Exception **messages are
not written**, only the type, because provider messages can embed submitted values. Every string passes through an
SSN-shaped-value scrubber as an additional safeguard; the primary control is logging safe fields only. EF command
and HTTP client categories are limited to warnings; no bodies are logged. Each request gets an operation ID scope and
an `X-Operation-Id` response header; interactive actions call `logger.BeginOperation(name)`. Operation IDs are always
fresh server-generated GUIDs, never the ambient trace ID: a Blazor circuit's activity is long-lived (every action would
share it) and an HTTP `traceparent` is client-controlled. TraceId/SpanId are logged alongside for trace correlation. `/health`, `/health/live` and
`/health/ready` (database + veteran lookup + attachment storage) are anonymous and answer JSON with a status per component
(`HealthResponseWriter`); any Unhealthy component makes the response 503. EventId ranges: E1 1000-1999, E2 2000-2999, E3 3000-3999,
E4 4000-4999, E5 5000-5999. OpenTelemetry traces/metrics/dashboard are optional and not included.

## Alternatives considered

- Framework JSON console formatter: its implementation is internal, so it cannot be wrapped for redaction.
- `Microsoft.Extensions.Compliance.Redaction`: only protects classified `[LogProperties]` fields, not arbitrary
  strings; may be adopted per field later (the package is pinned for that).
- OpenTelemetry exporters: outside the required scope and a possible external dependency.

## Tradeoffs

Dropping exception messages loses some diagnostic detail; the type, EventId and operation ID remain.
A nine-digit identifier in a log would be redacted even if it is not an SSN.

## Consequences

Epics define `LoggerMessage` events in their own range and log IDs and actors only.
