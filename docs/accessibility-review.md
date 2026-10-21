# Accessibility review record (E5-S9)

Target: WCAG 2.2 AA as the engineering yardstick for the primary workflow (A10). axe-core checks a subset of WCAG and
cannot establish conformance; this record combines automated evidence with manual review. A check is marked done only
when it was actually performed; open items are listed, not waived.

## Automated evidence

| Check | How | Result |
|---|---|---|
| axe-core, tags `wcag2a wcag2aa wcag21a wcag21aa wcag22aa` | `CaseIntake.E2E` scans sign-in, dashboard, case list, new case (with errors), case detail (also with a conflict), status dialog, access denied, Not Found, test run and execution detail, phone dashboard and new case, tablet and desktop list; Change 1: Users, Roles, role editor, user detail (editable and read-only), deactivate dialog, audit log, audit entry; all of these at 390 px as well | see the latest run in Test results and [verification.md](verification.md) |
| Keyboard workflow | `PrimaryWorkflowTests` drives the whole primary workflow with Tab/Enter/Space/Esc and asserts focus after each step (skip link, h1 after navigation, error summary, Title on edit, Edit after cancel/save, status heading after a change, trigger after a cancelled dialog, file input after a rejected upload) | as above |
| Keyboard administration (Change 1) | `RolesAndPermissionsTests` creates a role (Space on permission checkboxes), adds a user with it, finds and opens the user, sets a Deny override with the arrow keys in the radio group, and deactivates through the dialog (initial focus on Cancel, Esc returns focus to the trigger, focus on Reactivate afterwards); h1 focus after every navigation, including after creating a role | as above |
| Keyboard audit log (Change 1, part 2) | `AuditLogTests` (browser) opens the audit log from the navigation, filters it (focus stays on Apply after the query-only navigation, the result count is announced), opens an entry (h1 focused), follows a user's History link, and downloads the CSV with Enter | as above |
| Accessibility sweep (Change 1, part 5) | `AccessibilitySweepTests`: axe, one h1, title, `lang="en-US"` and a main landmark on all 22 signed-in pages and 3 public pages; skip link as first Tab stop, visible focus not hidden under the sticky header for 30 Tab stops per page; no sideways scrolling at 320 CSS px; times in EDT/EST | as above |
| Labels, `aria-invalid`, `aria-describedby`, live regions, focus calls | `CaseIntake.Web.Tests` (bUnit) | as above |
| Reflow and touch targets | responsive tests at 375x812, 390x844, 768x1024, 1440x900: no horizontal overflow, bottom-bar targets >= 44x44 px | as above |
| Contrast | design tokens chosen at >= 4.5:1 for text and >= 3:1 for UI and focus (app.css comments); axe `color-contrast` | as above |

## Manual review (to be performed and signed off by a reviewer)

The full procedures (M1 to M9) and their sign-off table are in [section-508/manual-test-procedures.md](section-508/manual-test-procedures.md); the Section 508 conformance report is [section-508/accessibility-conformance-report.md](section-508/accessibility-conformance-report.md).

| Check | Status | Notes |
|---|---|---|
| Native file picker opened and operated by keyboard (Space/Enter on the file input, choose a file, Upload) | Not yet performed | automated tests use programmatic file selection (`SetInputFiles`), which bypasses the native picker |
| Download prompt/behaviour with the keyboard (Enter on Download) | Not yet performed | automated test verifies the downloaded bytes, not the browser prompt |
| Screen-reader spot check (NVDA + Chrome or VoiceOver + Safari): field labels and hints, error summary, "Showing ... cases", "Case N created", "Status changed to ...", lookup outcomes, upload errors, reconnect dialog | Not yet performed | |
| Zoom 200% and 400% (reflow at 320 CSS px) on dashboard, list, intake, detail | Not yet performed | 375 px automated only |
| Visible focus on every control in light conditions and Windows high-contrast mode | Not yet performed | focus style: 2 px black outline with white ring; forced-colors rules in app.css |
| Empty, loading and error states read sensibly | Not yet performed | automated: bUnit for dashboard and list states |
| Screen reader on the administration pages (Change 1): role checkbox group with hints and the "can't add or remove" reason, permission table with row and group headers, per-permission radio groups ("Override for ..."), read-only reason, "Permissions saved." announcement | Not yet performed | automated: bUnit (legends, describedby, live region text) and axe on every page |
| Reconnect: stop `web` while a page is open (`docker compose stop web`), observe and hear the reconnect dialog, start it again | Not yet performed | |
| iOS Safari and Android Chrome on real devices (safe areas, keyboard types, bottom bar) | Not yet performed | Chromium device emulation only in automation |

## Known limitations

- On phones the signed-in name and role are in the profile sheet; the app bar shows initials whose accessible name
  includes the user (ADR 0011).
- Charts are SVG images with a text summary and a data table; the bars themselves are not individually focusable.
- A direct page load leaves focus at the start of the document (so the skip link is first); focus moves to the h1
  after in-app navigation (ADR 0011).
- The user detail page's permission table has three radio buttons per permission (16 permissions). Each radio group is
  one Tab stop and the arrow keys change the choice, as in any native radio group; the table scrolls horizontally inside
  its card on narrow screens instead of reflowing into cards (Change 1).
