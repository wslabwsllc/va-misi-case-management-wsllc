# 0011. UI kit, accessibility approach, responsive shell, dashboard definitions and PWA policy

* Status: accepted
* Owner: E5

## Context

The primary workflow must be keyboard accessible with clear labels, validation, focus behaviour and announced status
and errors (R-4.2-12, E5-S1/S2), at a WCAG 2.2 AA engineering target (A10, OQ-7). The enhancement brief adds a Power
Apps model-driven look on desktop, a native-app feel on phones (bottom navigation, cards, sheets, floating action),
a dashboard with KPIs and charts from real data, and an optional installable PWA, all in the same deployment.

## Decision

**Kit.** A small in-repo component kit (no UI framework): `LabeledInputText`/`LabeledTextArea` (derive from
`InputBase`, render `<label for>`, required/optional in text, hint, error and counter linked by `aria-describedby`,
`aria-invalid`, a counter announced politely after typing pauses), `FormErrorSummary` (focused after a failed submit,
links move focus to fields), `ErrorAlert` (`role="alert"`, region always present), `StatusAnnouncer` + outlet (one
persistent `role="status"` region that survives navigation and doubles as a toast), `ModalDialog` (native `<dialog>`
with `showModal()`: focus trap, inert page, Esc; focus returns to the trigger), `PageShell` (unique `<title>`, focusable
`h1`, breadcrumb and mobile back target), `StatusPill`, `KpiTile`, `EmptyState`, `LoadingIndicator`, `PagerControl`,
`Chart`. Design tokens are CSS custom properties in `wwwroot/app.css`; text pairs meet 4.5:1, UI and focus 3:1.

**Focus after navigation.** `FocusOnNavigation` (in `Routes.razor`) moves focus to the new page's `h1` on every in-app
navigation to a different page or record. The first page of a circuit (a full page load) keeps focus at the document
start, so the first Tab reaches "Skip to main content" (E5-S1 criterion 1); this resolves the tension with "focus moves
to the h1 when it loads" in favour of the skip link on a fresh load. Query-string-only changes (sort, filter, page)
keep focus on the control the user activated. The Not Found page applies the same rule.

**Responsive shell.** One layout, three bands: desktop (>= 1024 px) sidebar site map, header with brand, breadcrumb and
identity; tablet (768-1023 px) the same sidebar, collapsible to an icon rail, touch-sized targets; phone (< 768 px) a
sticky app bar (back to the logical parent, title, notifications, profile sheet) and a bottom bar (Home, Cases, My
Tasks, Notifications, More) above the safe area, record cards instead of tables, full-width 16 px inputs (no iOS zoom),
`inputmode` keyboards, a floating New case action only with `cases.create`, dialogs as bottom sheets. Hidden variants
use `display: none`, so each landmark exists once in the accessibility tree. On phones the signed-in name and role are
in the profile sheet (the app bar shows initials, whose accessible name includes the user).

**Navigation and permissions.** One site map (`AppNavigation`) feeds the sidebar, bottom bar and More sheet; each item
names an E3 policy and is shown only when `IAuthorizationService` grants it. Buttons and panels use `AuthorizeView`.
Hiding is a convenience: pages carry `[Authorize(Policy = ...)]` and services and endpoints authorize again.

**Dashboard definitions.** Figures come only from `ICaseService.ListAsync`, so its authorization applies; nothing is
hard-coded. *Open* = New, Triaged or In Progress; *Pending review* = Resolved (waiting to be closed); *Resolved cases* =
Resolved or Closed; *Overdue* = open and its due date is before today (Eastern calendar), the same rule as the case list's Overdue
filter (`CaseTriageRules.IsOverdue`); *Assigned to me* = open cases the user owns; *My tasks* = cases in a status from
which the user holds a transition permission (domain workflow edges plus E3 permissions, the same rule that produces
the status buttons); *Recent cases* = New cases created in the last `Dashboard:NewCasesDays` (7) days; monthly
resolution uses a case's last update; the priority and category charts count open cases. Charts are server-rendered
SVG (no chart library, nothing to dispose) with a caption, a text summary, value labels, legends, distinct marker shapes
and a data table. Issue #21 added: hover and keyboard-focus tooltips (CSS only, plus the SVG `title`); drill-through,
where each column or bar is a named link to the case list with the same filter (the SVG is then `role="group"` of links
instead of one `role="img"`); a grow-in animation that `prefers-reduced-motion` removes; an Expand button that opens a
larger copy in the modal dialog; and month axis labels that show the year once and thin out so they never overlap.

**Triage fields (issue #22).** Cases have a priority (Low, Medium, High, Urgent), a category, an owner (the user name
of someone whose role grants `cases.edit`, validated by the service against the Identity tables) and an optional due
date (a UTC calendar date). An empty due date at intake defaults to the priority's target (Urgent 2, High 5, Medium 10,
Low 30 days). Priority, category and due date changes are audited by field name like the text fields.

**Assignment.** The owner changes only through `ICaseAssignmentService.AssignAsync`, never through Edit, under
`CaseAssignmentRules` (shared by the service and the UI): a supervisor (`cases.assign`) assigns or reassigns any open
case to anyone who works cases, or unassigns it; a case worker (`cases.edit` without `cases.assign`) may only **Take**
an unassigned case or **Hand back** their own; intake clerks, auditors and QA cannot change the owner; Closed cases keep
theirs. Assignment is not tied to a status: it can happen at any time before the case closes. Choosing an owner at
intake is an assignment too, so only supervisors see the owner picker there. Each change is an `Assigned` audit entry
("Assigned to X (was Y)"), logged as EventId 2004, and the new owner's notification feed shows it as "Assigned to you".
The detail page offers Assign/Reassign (dialog), Take case or Hand back in its command bar, and the case list's row
menu offers "Assign…" to supervisors. The seeded demo cases spread their due dates from June 2026 over 420 days,
so the demo shows a mix of overdue and upcoming cases instead of everything overdue; the E5 triage migration gives
seeded rows of existing databases the same values (checked by `CaseTriageBackfillTests`).

**Notifications (issue #21).** The feed is the append-only audit trail: every entry written by someone else in the last
`Notifications:WindowDays` (14) days on a case the user can see. A per-user "read up to" time
(`CaseNotificationCursors`) marks entries after it as unread; "Mark all as read" moves it forward. The sidebar and bottom
bar show the unread count. A process-wide `CaseActivitySignal` makes every open circuit re-read its own count (through
the authorized service) right after any case change, with a one-minute poll as a fallback; the signal carries no data.
With several web instances the signal would need a shared bus; until then other instances catch up within a minute.

**Progress feedback (issue #21).** Every page wraps its loads, refreshes and saves in `PageActivity`; the layout shows a
thin indeterminate bar while any work runs (hidden from assistive technology, because each page announces its own
loading and results). Refresh buttons show "Refreshing…" for at least a moment, announce the result and briefly
highlight the refreshed figures.

**PWA and offline policy.** `manifest.webmanifest` (standalone, theme colour, icons including maskable and Apple touch
icons) and `sw.js`. The worker caches only static, same-origin assets (CSS, JS, icons, the manifest, `offline.html`),
network first. It never caches page HTML, the circuit (`/_blazor`), sign-in/out (`/account`), attachment or evidence
downloads, exports or health endpoints. When the network is unreachable a navigation shows the static offline page.
Browsers register service workers only on HTTPS or localhost; elsewhere the app works without one.

## Alternatives considered

- A component library (Fluent UI Blazor, MudBlazor): faster widgets, but another dependency to audit for accessibility
  and CSP, and the brief asks to avoid unnecessary dependencies.
- The framework's `FocusOnNavigate`: focuses the `h1` on the first render too (conflicts with the skip-link criterion)
  and only when the page type changes (not between two records).
- A JavaScript chart library: richer charts, but CSP, bundle size and disposal concerns for simple bar and line charts.
- Separate mobile pages or app: duplicates behaviour and authorization.

## Tradeoffs

Hand-written components need their own tests (bUnit, Playwright, axe) and stay deliberately small. Rendering both
the table and the phone cards doubles list markup for at most 50 rows. axe covers a subset of WCAG; manual keyboard and
screen-reader review is still required and recorded separately ([accessibility-review.md](../accessibility-review.md)).

## Consequences

New pages start with `PageShell` and use the kit, so title, `h1` focus, breadcrumb and back target come for free. New
navigation entries go into `AppNavigation` with their policy. The dashboard definitions are documented in the
assumptions log and change only with the case model.
