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
hard-coded. The case model has no assignee, priority, category or due date, so: *Open* = New, Triaged or In Progress;
*Pending review* = Resolved (waiting to be closed); *Resolved cases* = Resolved or Closed; *Overdue* = open and not
updated for `Dashboard:OverdueAfterDays` (14) days; *Assigned to me / My tasks* = cases in a status from which the
user holds a transition permission (domain workflow edges plus E3 permissions, the same rule that produces the status
buttons); *Notifications* = cases updated in the last `Dashboard:RecentActivityDays` (14) days; monthly resolution
uses a case's last update. The priority and category charts state that the data does not exist instead of inventing
it. Charts are server-rendered SVG (no chart library, nothing to dispose) with a caption, a text summary, value labels,
legends, distinct marker shapes and a data table.

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
