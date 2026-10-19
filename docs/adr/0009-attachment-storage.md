# 0009. Attachment storage and protection

* Status: accepted
* Owner: E4

## Context

Spec 4.2 requires attachment upload and download with role-based permissions; uploads are part of the case audit
information. The spec sets no size, type, count or storage rules (OQ-6), and puts enterprise malware scanning out of
scope (4.7). The app must survive container recreation (`docker compose up` again keeps data) and run with no external
services (4.5). Attachments are uploaded from an Interactive Server component and downloaded over plain HTTP.

## Decision

**Storage.** Metadata in PostgreSQL (`Attachments`: GUID id, case FK, sanitized display name, allowlisted content
type, size, SHA-256, storage key, UTC upload time, uploader); bytes on the `attachments` named volume, outside
wwwroot, under `blobs/{shard}/{key}`, where the shard is the key's last two hex digits (the random part of a
version-7 GUID; its leading digits are a timestamp and would put every file in one directory). The key is a server-generated GUID, never the user's file name, and is
validated before any path is built. Every metadata column is write-once; there is no delete operation (OQ-17).

**Limits (configurable, validated at startup).** `Attachments:MaxBytes` (default 10 MiB) and `Attachments:AllowedTypes`
as pairs of MIME type and extensions: PDF, PNG, JPEG (`.jpg`/`.jpeg`), plain text, DOCX. Both the claimed type and
the extension must match one pair; the file is stored and served with the allowlisted type, not the browser's raw
claim. Empty files and uploads to Closed cases are rejected (D7). The first bytes must also match the claimed type for
PDF (`%PDF-`), PNG, JPEG and DOCX (ZIP); plain text has no signature and is accepted on type and extension alone. Messages state the allowed types and the limit.

**Upload sequence.** Authorize (`attachments.upload`) → validate name, type and declared size → load the case →
stream into `tmp/{random}.part`, counting bytes and hashing incrementally, stopping as soon as more bytes than
declared arrive, and force it to disk → move atomically to the final key → in **one** transaction, take a share lock
on the case row (`FOR SHARE`), re-check that the case is not Closed, and commit the metadata and the
`AttachmentUploaded` audit entry. The lock means a case closed while the file was streaming gets no attachment, and a
concurrent status change waits for the commit. From the commit onwards the caller's cancellation no longer applies,
so a COMMIT is never abandoned half-way. The audit details hold the attachment id, size and type, never the file
name (D8). A failing source stream (client disconnect, browser timeout) is reported as `UploadIncomplete`; a storage
failure (disk full, permissions) is logged as an error (4109) and surfaces as a server error.

**Compensation.** A database and a filesystem cannot share a transaction. If the commit throws, the service checks
with a fresh context whether the attachment row exists: present → the upload succeeded (e.g. a lost acknowledgement);
absent → the file is deleted (a failed delete is logged and left for reconciliation) and the error propagates; the
check itself fails → the file is kept and the caller gets `UploadOutcomeUnknown` ("refresh before retrying").

**Reconciliation.** The migrator runs `AttachmentStorageReconciler` before web starts. It deletes temporary files and
content without metadata once older than `Attachments:OrphanGracePeriod` (1 h), never deletes content that has
metadata, and reports attachments whose content is missing. Failures are logged and retried next start.

**Download.** `GET /cases/{caseId}/attachments/{id}/content` requires the `attachments.download` policy (401 anonymous,
403 without permission, no login redirect). The service re-checks the permission for the HTTP actor and returns only an
attachment of that case (another case's ID is a 404, like an unknown ID). The response is always
`Content-Disposition: attachment` with an RFC 5987 encoded name, the allowlisted type, `X-Content-Type-Options: nosniff`
and `Cache-Control: no-store`; range requests are supported. A completed download is logged (4103); missing content is
a controlled 404 with a diagnostic event (4105) and no path.

## Alternatives considered

- **PostgreSQL `bytea`/large objects.** One transaction for bytes and metadata, so no compensation or reconciliation.
  Rejected: 10 MiB rows bloat the database, backups and the EF change tracker, and streaming is awkward.
- **Object storage (S3/MinIO).** The production-shaped choice in VAEC, but another container and SDK for no benefit
  in a local evaluation. The internal `IAttachmentStore` keeps this swap contained to one class.
- **Full content parsing or malware scanning.** Out of scope (4.7). The leading-byte signature check above catches
  renamed files cheaply but is not proof of safety; served-as-download, `nosniff` and the allowlist limit exposure.

## Tradeoffs

- A crash between moving the file and committing (or between a failed commit and the delete) can leave an
  unreferenced file until the next start; it is never served because no metadata points at it.
- If the connection is lost *during* COMMIT, the server may still complete it after the service's existence check
  ran; the file would then have been deleted and the attachment downloads as a controlled 404 (4105), which the
  reconciler reports as missing content. Not cancelling the commit closes the common case of this window.
- The stored SHA-256 is recorded but not re-verified on every download; the file size is compared with the
  metadata on every read, so truncated or replaced content is refused (404, event 4112) rather than served.
- Outside Compose, `Attachments:RootPath` defaults to `<system temp>/caseintake-attachments`, so a locally run web
  app and migrator use the same folder whatever their working directory.
- The file's bytes are forced to disk before the commit, but the directory entry created by the move into `blobs/`
  is not (.NET has no portable directory fsync). After a power loss right after a commit, the content could be back
  under `tmp/` and be removed as stale by the reconciler; the attachment would then download as a controlled 404.
- If the execution strategy retries after a COMMIT that did reach the database and the case was closed in between,
  the commit is confirmed with a fresh lookup before anything is deleted. Covered by
  `When_a_retry_finds_the_case_closed_after_its_first_commit_landed_the_upload_is_reported_as_done` and its
  unconfirmable counterpart, which simulate the retry with an EF command interceptor.
- MIME type and extension are claims by the browser; they are not proof of content.
- Uploads add only an audit row, so they never bump the case's concurrency version or conflict with edits.

## Consequences

- The `attachments` volume is mounted on both `migrator` (reconciliation) and `web`.
- Swapping storage means a new `IAttachmentStore` implementation; the service and contracts do not change.
- E5's attachments panel passes `IBrowserFile.OpenReadStream(maxAllowedSize)` and maps the `File` field errors.
