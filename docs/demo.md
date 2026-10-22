# Demonstration walkthrough

Covers every demonstration item of plan section 9.1 with the synthetic accounts and inputs below and the real running
application (no scripted screens). Start from a clean state with `docker compose up --build` and open
<http://localhost:8080>. All data is synthetic.

## Accounts and inputs

| Account | Password | Role | Use it to show |
|---|---|---|---|
| `auditor@example.test` | `Demo-Passw0rd!` | Auditor | read-only: cases, audit trails, downloads; no New case, edit, status change or upload |
| `intake@example.test` | `Demo-Passw0rd!` | IntakeClerk | open cases, lookup, upload; no edit or status change |
| `caseworker@example.test` | `Demo-Passw0rd!` | CaseWorker | intake, edit, lookup, attachments, start/resolve; cannot triage, close or reopen |
| `supervisor@example.test` | `Demo-Passw0rd!` | Supervisor | everything, including triage, close and reopen |
| `qa@example.test` | `Demo-Passw0rd!` | QualityAnalyst | read cases; test results and evidence (Administration > Quality assurance) |

| Lookup SSN | Result |
|---|---|
| `900-12-3456` | Found: Rowan Calloway |
| `999-99-9999` | Not found |
| `12345` | Invalid SSN format (no request sent) |

Attachments: any small `.pdf`, `.png`, `.jpg`, `.txt` or `.docx` under 10 MB; an `.exe` or a renamed file is rejected.

## Walkthrough (keyboard friendly; mouse works too)

1. **Sign in** as the supervisor. Press Tab once on any freshly loaded page: "Skip to main content" appears. The header
   shows the user name, the role **Supervisor** and **Sign out**. The dashboard shows Total, Open, Pending review,
   Overdue (open and past the due date), Resolved and Assigned to me (open cases you own), the case volume and resolution
   trends, cases by status, open cases by priority and by category, and the work lists. Hover over or Tab to a column:
   its value appears; activate it to open the matching cases. *Expand* opens a larger chart. *Refresh* shows
   "Refreshing…", then announces the new time. Recent cases lists only New cases from the last 7 days.
2. **List controls**: Cases > tick *In Progress*, Apply (the count is announced: "Showing 1–n of N cases"); activate the
   *Title* header twice (ascending, then descending); Next page; reload the browser: the same view returns from the URL.
   Search "Benefit inquiry" to filter by text; a search with no match shows the empty state with *Clear filters*.
3. **Validation and intake**: New case > Create case with an empty title: the error summary takes focus and the Title
   field explains the rule. Enter a title and a description (counters show the limits). In *Veteran lookup* enter
   `999-99-9999` (Not found), `12345` (invalid format alert), then `900-12-3456`: Rowan Calloway is found; choose
   **Use this veteran**. The SSN box is empty again; only "SSN ending 3456" is kept. Choose a priority (the due-date hint
   shows the default target), a category and an owner. Create the case: the detail page opens and "Case N created" is
   announced.
4. **Case detail and audit information**: the summary strip shows status, priority, category, owner, due date (with
   "Due in n days" or "Overdue by n days") and last change; the stepper shows the workflow stage. The tabs (arrow keys
   move between them) hold the Overview with Created/Updated (ET) and by, the Workflow status history, the
   Attachments and the History (audit trail, 10 entries per page).
5. **Edit**: Edit > change the title > Save ("Changes saved"). Edit > Cancel restores the values. To show the conflict
   handling, open the same case in a second browser as the case worker and save there first; saving in the first
   browser explains the conflict, offers **Reload latest** and keeps your unsaved text.
6. **All five transitions** (supervisor): Move to Triaged (confirm in the dialog; Esc cancels and returns focus), Move to
   In Progress, Move to Resolved, **Reopen (move to In Progress)**, Move to Resolved, Move to Closed. Each change is
   announced, the status heading takes focus, and the audit trail grows. A Closed case has no Edit and no status buttons.
7. **Role difference**: sign out, sign in as the case worker. A New case shows "No status changes available" (no triage),
   a Resolved case offers neither Close nor Reopen; Triaged and In Progress cases offer Start/Resolve. Sign in as
   `qa@example.test`: no New case, but Administration > Quality assurance; opening `/cases/new` shows Access denied
   with a link back to the case list. Sign in as `intake@example.test`: New case and Upload are offered, but a case has
   no Edit and no status buttons. Sign in as `auditor@example.test`: cases and the audit trail are readable and
   downloads work, but nothing can be changed and `/cases/new` shows Access denied.
   **Users** (supervisor): Administration > Users, add `clerk@example.test` as Intake clerk with an initial password,
   sign in as them, choose **Change password** in the header and sign in again with the new password.
8. **Lookup**: shown in step 3 against the real `veteran-lookup` container (`POST /veterans/lookup`).
9. **Upload and download**: on an open case open the Attachments tab, choose a file and use **Remove** to take it back;
   choose a `.txt` file and Upload ("File uploaded" is announced and the row appears); upload an `.exe` to see the
   rejection. Use **Download** on the row; the file is identical.
9a. **Assignment and notifications**: as the supervisor open an unassigned case and choose **Assign** (also in the
   case list's ⋮ menu): pick the case worker. With the case worker signed in in a second browser, their bell and
   Notifications badge update without a reload, and the Notifications page shows "assigned this case to you" with an
   **Assigned to you** label. As the case worker, an unassigned case offers **Take case** and your own case offers
   **Hand back**; someone else's case offers neither. **Mark all as read** clears the badge.
10. **Phone and tablet**: narrow the window below 768 px (or use the browser's device toolbar): bottom navigation,
    app bar with back button, case cards, the floating New case button and sheets for More and Profile.
11. **Test evidence**: run `scripts/run-all-tests.sh` (or `docker compose run --rm tests`), sign in as
    `qa@example.test`, open Test results: metrics, trends, failures by module, browser/device coverage, runs, a test's
    steps, screenshots (preview and zoom), trace, logs, history, CSV/HTML export.
12. **Operational logs**: `docker compose logs web` shows JSON events, for example 3001 LoginSucceeded, 2001
    CaseCreated, 2002 CaseUpdated, 2003 CaseStatusChanged, 2005 CaseVersionConflict, 4101 AttachmentUploaded, 4103 AttachmentDownloaded, 3005 AuthorizationDenied,
    5101 CasesExported, 5201 TestRunImported. No SSN or password appears (`docker compose logs web | grep -E '[0-9]{3}-[0-9]{2}-[0-9]{4}'` finds nothing).

### Q&A extras (optional, not part of the walkthrough)

- **The lookup service serves the Government contract verbatim** (spec 4.6, ADR 0008). The mock is not published to
  the host, so ask it from inside its container and compare hashes. Both commands print
  `bbd95fc2a187220e9bfc1860c84fbf148bfc8b10282ae82f13c05bbb84730f26`:

  ```bash
  docker compose exec -T veteran-lookup bash -c 'exec 3<>/dev/tcp/127.0.0.1/8081; printf "GET /openapi/v1.yaml HTTP/1.0\r\nHost: localhost\r\n\r\n" >&3; while IFS= read -r line <&3 && [ ${#line} -gt 1 ]; do :; done; cat <&3' | shasum -a 256
  shasum -a 256 docs/contracts/veteran-lookup.spec.openapi.yaml
  ```

## Restart and persistence

`docker compose down` then `docker compose up -d`: cases, edits, attachments, sign-in cookies (Data Protection keys)
and test history are still there. `docker compose down -v` deletes all of it.
