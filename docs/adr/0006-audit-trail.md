# 0006. Audit information: audit fields plus an append-only trail

* Status: accepted
* Owner: E2

## Context

Spec 4.2 asks for "case intake with audit information" without defining it (OQ-2). Supervisors need to see who
changed what and when; attachments (E4) also need audit entries. Interactive Server circuits live long, so audit
writes must not depend on a long-lived context, and a business change must never commit without its audit entry
(or the other way round).

## Decision

- Each case carries `CreatedAtUtc/CreatedBy/UpdatedAtUtc/UpdatedBy` (UTC `timestamptz`, user name of the actor).
- `CaseAuditEntries` is an append-only table (`bigint` identity, FK to `Cases` with `ON DELETE RESTRICT`) holding
  UTC time, actor ID and name, action (`Created`, `Edited`, `StatusChanged`, `Reopened`, `AttachmentUploaded`),
  optional from/to status and a short safe details string (changed field names; never description text, veteran
  details or SSNs).
- `Persistence.Auditing.AuditAppender.Append(db, case, actor, auditEvent)` adds the entry to the caller's context and
  never saves. The service performs the business change and the append in one `SaveChanges` (one transaction). A
  forced database failure on the audit insert leaves no case row (integration-tested).
- `AppDbContext.SaveChanges*` rejects modified or deleted `CaseAuditEntry` instances
  (`AuditTrailModificationException`); there is no update or delete API.
- Rejected operations (validation, permission, conflict, invalid transition) and no-op edits write nothing.
- `GetAuditTrailAsync` returns entries oldest first (time, then ID).

## Alternatives considered

- Audit fields only: does not show history of status changes.
- EF Core temporal tables / PostgreSQL triggers writing history: captures every column change but loses the
  application actor unless it is passed through session variables, and makes "who did it" harder to test.
- Event sourcing: far more than the challenge needs.

## Tradeoffs

Immutability is enforced in the application, not against a database administrator (out of scope). Details are a
short string rather than a structured diff, which keeps sensitive values out of the trail.

## Consequences

E4 calls `AuditAppender.Append` with its own operation context for uploads. Any new mutation must append exactly one
entry in the same save.
