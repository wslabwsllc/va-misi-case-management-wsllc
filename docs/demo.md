# Demonstration walkthrough

Covers every demonstration item of plan section 9.1 with the synthetic accounts and inputs below and the real running
application (no scripted screens). Start from a clean state with `docker compose up --build` and open
<http://localhost:8080>. All data is synthetic.

## Accounts and inputs

| Account | Password | Role | Use it to show |
|---|---|---|---|
| `caseworker@example.test` | `Demo-Passw0rd!` | CaseWorker | intake, edit, lookup, attachments, start/resolve; cannot triage, close or reopen |
| `supervisor@example.test` | `Demo-Passw0rd!` | Supervisor | everything, including triage, close and reopen |
| `qa@example.test` | `Demo-Passw0rd!` | QualityAnalyst | read cases; test results and evidence (Administration > Quality assurance) |

| Lookup SSN | Result |
|---|---|
| `900-12-3456` | Found: Rowan Calloway |
| `999-99-9999` | Not found |
| `12345` | Invalid SSN format (no request sent) |

Attachments: any small `.pdf`, `.png`, `.jpg`, `.txt` or `.docx` under 10 MiB; an `.exe` or a renamed file is rejected.

## Walkthrough (keyboard friendly; mouse works too)

1. **Sign in** as the supervisor. Press Tab once on any freshly loaded page: "Skip to main content" appears. The header
   shows the user name, the role **Supervisor** and **Sign out**. The dashboard shows Total, Open, Pending review,
   Overdue, Resolved and Assigned to me (definitions under each figure), the case volume and resolution trends, cases by
   status, and the work lists. Priority and category say "Not available" because cases have no such fields.
2. **List controls**: Cases > tick *In Progress*, Apply (the count is announced: "Showing 1–n of N cases"); activate the
   *Title* header twice (ascending, then descending); Next page; reload the browser: the same view returns from the URL.
   Search "Benefit inquiry" to filter by text; a search with no match shows the empty state with *Clear filters*.
3. **Validation and intake**: New case > Create case with an empty title: the error summary takes focus and the Title
   field explains the rule. Enter a title and a description (counters show the limits). In *Veteran lookup* enter
   `999-99-9999` (Not found), `12345` (invalid format alert), then `900-12-3456`: Rowan Calloway is found; choose
   **Use this veteran**. The SSN box is empty again; only "SSN ending 3456" is kept. Create the case: the detail page
   opens and "Case N created" is announced.
4. **Audit information**: on the detail page note Created/Updated (UTC) and by, and the audit trail.
5. **Edit**: Edit > change the title > Save ("Changes saved"). Edit > Cancel restores the values. To show the conflict
   handling, open the same case in a second browser as the case worker and save there first; saving in the first
   browser explains the conflict, offers **Reload latest** and keeps your unsaved text.
6. **All five transitions** (supervisor): Move to Triaged (confirm in the dialog; Esc cancels and returns focus), Move to
   In Progress, Move to Resolved, **Reopen (move to In Progress)**, Move to Resolved, Move to Closed. Each change is
   announced, the status heading takes focus, and the audit trail grows. A Closed case has no Edit and no status buttons.
7. **Role difference**: sign out, sign in as the case worker. A New case shows "No status changes available" (no triage),
   a Resolved case offers neither Close nor Reopen; Triaged and In Progress cases offer Start/Resolve. Sign in as
   `qa@example.test`: no New case, but Administration > Quality assurance; opening `/cases/new` shows Access denied
   with a link back to the case list.
8. **Lookup**: shown in step 3 against the real `veteran-lookup` container (`POST /veterans/lookup`).
9. **Upload and download**: on an open case choose a `.txt` file and Upload ("File uploaded" is announced and the row
   appears); upload an `.exe` to see the rejection. Use **Download** on the row; the file is identical.
10. **Phone and tablet**: narrow the window below 768 px (or use the browser's device toolbar): bottom navigation,
    app bar with back button, case cards, the floating New case button and sheets for More and Profile.
11. **Test evidence**: run `scripts/run-all-tests.sh` (or `docker compose run --rm tests`), sign in as
    `qa@example.test`, open Test results: metrics, trends, failures by module, browser/device coverage, runs, a test's
    steps, screenshots (preview and zoom), trace, logs, history, CSV/HTML export.
12. **Operational logs**: `docker compose logs web` shows JSON events, for example 3001 LoginSucceeded, 2001
    CaseCreated, 2002 CaseUpdated, 2003 CaseStatusChanged, 2005 CaseVersionConflict, 4101 AttachmentUploaded, 4103 AttachmentDownloaded, 3005 AuthorizationDenied,
    5101 CasesExported, 5201 TestRunImported. No SSN or password appears (`docker compose logs web | grep -E '[0-9]{3}-[0-9]{2}-[0-9]{4}'` finds nothing).

## Restart and persistence

`docker compose down` then `docker compose up -d`: cases, edits, attachments, sign-in cookies (Data Protection keys)
and test history are still there. `docker compose down -v` deletes all of it.
