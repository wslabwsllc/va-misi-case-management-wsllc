# 0015. Stored error log, System Health and error pages

* Status: accepted (extends [0003](0003-telemetry-and-logging.md))
* Owner: E3

## Context

Change 1, part 3 asks for errors to be kept in the database with a viewer and status tracking, a System Health page,
and error pages users can quote. Until now errors existed only as JSON lines on stdout ([ADR 0003](0003-telemetry-and-logging.md)),
and the `/error` page showed the activity trace ID, which matched nothing the team could look up.

## Decision

**Capture.** `ErrorCaptureLoggerProvider` (ServiceDefaults) receives every log event at Error or Critical and hands a
`CapturedError` to `IErrorLogSink` without blocking. It applies the console formatter's rules: the rendered message with
SSN-shaped values redacted, the exception's type and stack frames (also redacted) and never the exception message, which
can echo submitted values. It records the ambient operation ID ([ADR 0014](0014-system-audit-log.md)) and the request
path. Capture never throws into the code that logged. Hosts without a sink (the migrator) capture nothing.

**Store.** `ErrorLogWriter` (Identity, hosted service) owns a bounded queue of 1,000 errors (new ones are dropped when
it is full) and saves batches to `ErrorLogEntries` (migration `C3_ErrorLog`). It writes with `ErrorCaptureSuppression`
active, so a database outage cannot feed its own errors back into the queue; a batch that cannot be saved is dropped
and counted in a console warning (`ErrorLogWriteFailed`, 3040). The queue is drained on shutdown. Once a day it deletes
entries older than `ErrorLog:RetentionDays` (default 90; `ErrorLogPurged`, 3041). Captured columns reject updates; only
the status, who changed it, when and a note change, under the `xmin` row version. A fingerprint (category, event,
exception type and first stack frame) groups occurrences of the same error.

**Error pages.** The operation middleware now runs outermost, so the operation ID is still current when the exception
handler logs an unhandled exception and when `/error` renders. The page shows that ID as its reference (it was the
trace ID before) and says the error was recorded; Errors finds it by reference. The circuit error banner also says the
error was recorded. Not Found and Access denied pages are unchanged.

**Viewer** (`errors.view`, Administration > Errors): newest first, filters for Eastern dates, level, status, the same
error (fingerprint) and a text search over message, source, exception, page and reference (operation ID). The detail
page shows every field, the stack trace in a keyboard-scrollable region, how often the same error occurred, and a link
to the audit entries of the same operation. With `errors.manage`, the status moves between Open, Acknowledged and
Resolved with an optional note; a stale version is a Conflict, and each change is an `ErrorStatusChanged` audit entry.

**System Health** (`system.health`): runs the registered health checks (database, veteran lookup, attachment storage)
when opened and on "Check again"; shows version, environment, start time and uptime, whether the database answers,
applied and pending migrations, open errors, error counts and failed sign-ins, lockouts and access denials of the last
24 hours, and the latest errors. Overall is Unhealthy when the database does not answer or migrations are pending.
Every state is text plus an icon, never colour alone.

**Permissions.** `errors.view`, `errors.manage` and `system.health` are granted by default to Administrator only; the
seed adds them to existing databases (ADR 0014's applied-defaults rule).

## Alternatives considered

- **A database logging provider writing synchronously:** every error would wait on the database, and a database
  outage would block requests and recurse.
- **Storing exception messages:** most useful for debugging, but they can contain submitted values (SSNs); the stack
  trace and the operation ID lead to the code and the logs instead.
- **One row per distinct error with a counter:** compact, but loses the per-occurrence time, page and reference that
  support needs; the fingerprint gives the grouping.

## Tradeoffs

- Errors logged while the queue is full, or in a batch that fails to save, are lost from the store (still on stdout).
- Errors from circuits and background work have no request path; circuit errors have no operation ID.
- Retention deletes old entries regardless of status.

## Consequences

- Anything logged at Error or above appears in Administration > Errors: log at Error only what someone should look at.
- New code that writes the error store must run under `ErrorCaptureSuppression`.
