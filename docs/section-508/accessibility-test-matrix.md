# Accessibility test matrix (redesign)

Scope: the redesigned application (navy sidebar shell, dashboard, All cases, Case details, New case and Edit, My tasks,
Notifications, Case reports, Users, Sign in, password help, QA test results with the Section 508 filter, administration
pages and the error pages). Standards: Revised Section 508 (36 CFR 1194, which incorporates WCAG 2.0 Level A and AA by
reference, E205.4 and 502), and WCAG 2.2 Level AA as the project's engineering target.

**How to read this table.** "Automated" rows are checked by the test suites named in the Evidence column and were run
for this change (see [Results](#results)). "Manual" rows list the procedure to follow; a manual row is marked passed only
when a person has performed it and signed it off in [manual-test-procedures.md](manual-test-procedures.md). Rows that
were **not performed** say so. An automated scan cannot establish conformance, and this matrix is not a Section 508
certification or a conformance claim.

Status values: **Automated: passed** (the named tests passed in the recorded run) · **Automated: failed** ·
**Not run** (not executed for this change) · **Not performed** (manual, no tester yet) · **Partial** (part covered,
rest not performed).

## Section 508 baseline (WCAG 2.0 A/AA through E205.4) and project WCAG 2.2 AA checks

| # | Requirement | 508 / WCAG | How it is met in the redesign | Method | Evidence | Status |
|---|---|---|---|---|---|---|
| 1 | Full keyboard operation, no traps | E205.4 · 2.1.1 A, 2.1.2 A | Native links, buttons, selects and radios; dialogs are native `<dialog>` with Esc; tabs use arrow keys; chart slices are mouse targets with keyboard-equivalent legend links | Automated + Manual | `PrimaryWorkflowTests` (whole workflow by keyboard), `RedesignInteractionTests`, `CaseTriageAndNotificationUiTests` (tabs); manual A11Y-KB-001 | see Results; manual A11Y-KB-001 **Not performed** |
| 2 | No positive tabindex; skip link first | 2.4.1 A, 2.4.3 A | Only `tabindex="-1"` (headings, panels, chart slices) and `0` (tab panels); skip link is the first child of the shell | Automated | `AccessibilitySweepTests.On_every_page_the_skip_link_comes_first…` | see Results |
| 3 | Visible focus, never obscured | 2.4.7 AA · 2.4.11 AA (2.2) | 2 px outline with a white gap (white on the navy sidebar); `scroll-padding-top` keeps focus clear of the sticky header | Automated + Manual | `AccessibilitySweepTests` (30 Tab stops per page, ring present, not under the header); manual A11Y-KB-003 | see Results; manual **Not performed** |
| 4 | Card hover effects also on keyboard focus; discoverable on touch; reduced motion | 1.4.13 AA, 2.3.3 AAA (advisory) | `.icard:hover, .icard:focus-visible` share the lift, tint, accent and "View details ›"; `@media (hover: none)` always shows the hint; `prefers-reduced-motion` removes the lift and animations | Automated | `RedesignInteractionTests` (hint opacity 0 → 1 on focus and hover), `DashboardTests.Kpi_cards_are_single_links…` | see Results |
| 5 | No nested interactive controls in cards | 4.1.2 A | Each KPI/report card is one `<a>` with text spans only | Automated | `DashboardTests.Kpi_cards_are_single_links_with_a_details_hint_and_no_nested_controls`, `RedesignInteractionTests` | see Results |
| 6 | Dialogs: named, focus in and trapped, background inert, Esc, focus restored, Cancel first for destructive and submit confirmations | 2.4.3 A, 4.1.2 A | `ModalDialog` uses `showModal()` (inert background, focus trap, Esc) with `aria-labelledby`/`describedby`; `ConfirmSubmitDialog` and Remove user put `autofocus` on Cancel; focus returns to the trigger | Automated + Manual | `NewCaseTests.A_valid_submit_first_asks_for_confirmation…`, `UsersPageTests.Remove_asks_first_with_cancel_first…`, `RedesignInteractionTests` (Esc, focus back on Expand and on Submit), `PrimaryWorkflowTests` (status dialog); manual A11Y-KB-002 | see Results; manual **Not performed** |
| 7 | Semantics: landmarks, headings, tables, lists | 1.3.1 A, 2.4.6 AA | One banner, labelled navigation ("Main navigation", "Breadcrumb"), one main; one h1 per page; data tables with `th scope` and `aria-sort`; tab lists; ordered stepper with `aria-current="step"` | Automated | `AccessibilitySweepTests` (h1, title, lang, main on every page), `LayoutTests`, `CaseListTests` | see Results |
| 8 | Labels and instructions; no placeholder-only labels; paste allowed | 1.3.1 A, 3.3.2 A | Every input has a visible `<label>`; hints tied by `aria-describedby`; no paste blocking anywhere | Automated | `NewCaseTests`, `AccessDeniedAndAccountTests.Sign_in_page_labels_every_field…`, axe `label` rule | see Results |
| 9 | Error identification and summary | 3.3.1 A, 3.3.3 AA | `FormErrorSummary` takes focus and links to fields; fields get `aria-invalid` and their error text | Automated + Manual | `NewCaseTests`, `CaseDetailTests.Invalid_edit…`, `CaseTriageAndNotificationUiTests.A_server_error_on_a_triage_field…`; manual A11Y-SR-002 | see Results; manual **Not performed** |
| 10 | Date format explained; keyboard-operable date entry | 3.3.2 A, 2.1.1 A | `DateInput`: text field (keyboard and paste), visible "Format: MM-DD-YYYY" hint in `aria-describedby`, inline error for impossible dates, nothing applied until valid | Automated | `DateTimeFormatTests` (parsing), `CaseListTests.Due_date_range_is_typed_as_MM_DD_YYYY…`, `CaseListTests.A_date_that_is_not_real…`, `RedesignInteractionTests` | see Results |
| 11 | Auto-applied filters announce result counts politely | 4.1.3 AA | Polite live regions: "N cases found. Showing…" (All cases), "Dashboard filtered: N cases." (dashboard), result counts on Users and QA lists | Automated + Manual | `CaseListTests.Filters_apply_as_they_change…`, `DashboardTests.Filters_live_in_the_url…`, `UsersPageTests.Filters_apply_as_they_change…`, `Section508FilterTests`; manual A11Y-SR-001 | see Results; manual **Not performed** |
| 12 | Text contrast 4.5:1, large text and UI 3:1 | 1.4.3 AA, 1.4.11 AA | Tokens in `app.css` (`--text-secondary` 5.9:1, `--brand` 5.0:1 on white, red buttons 4.8:1, status pill pairs ≥ 4.5:1); the In Progress badge uses dark text on yellow | Automated + Manual | axe `color-contrast` on every page (`AccessibilitySweepTests`); manual A11Y-VIS-003 | see Results; manual **Not performed** |
| 13 | Colour is never the only cue | 1.4.1 A | Status and priority are always text badges with a dot or swatch; charts print values and shares; overdue states are words ("Overdue by 4 days") | Automated | `DashboardTests.Status_is_a_donut…` (legend text), `CaseListTests` (due state text) | see Results |
| 14 | Resize to 200% and reflow at 320 CSS px | 1.4.4 AA, 1.4.10 AA (2.1) | Fluid grids, filter grids that stack, tables in scroll containers, phone shell below 768 px | Automated + Manual | `AccessibilitySweepTests.Every_page_reflows_at_320_css_pixels…`, `ResponsiveLayoutTests`; manual A11Y-VIS-001 | see Results; manual **Not performed** |
| 15 | Forced colours (Windows high contrast) | 508 502.2.1 · 1.4.1 A | `@media (forced-colors: active)` gives buttons, cards, badges and chips system-colour borders, keeps focus and tooltips visible, hides decorative layers | Manual | A11Y-VIS-002 | **Not performed** |
| 16 | Reduced motion | 2.3.3 AAA (advisory) | `prefers-reduced-motion: reduce` disables transitions, chart animations and the card lift | Manual (code review) | `app.css` | **Not performed** (no tester run) |
| 17 | Accessible tooltips: on hover and focus, hoverable, dismissible | 1.4.13 AA | Icon buttons repeat their text name as a `data-tooltip` tip (part of the control, no gap); Esc hides it (`app.js`); chart tooltips the same, and the values are also in text | Automated | `TriageAndNotificationTests` (chart tooltip shown on hover, hidden by Escape) | see Results |
| 18 | Target size | 2.5.8 AA (2.2) | Controls 40 px (44 px on touch and phones); bottom navigation ≥ 44 px | Automated | `ResponsiveLayoutTests` (bottom bar ≥ 44×44), axe `target-size` | see Results |
| 19 | Accessible charts: title, summary, data table, keyboard drill-down, expand | 1.1.1 A, 1.3.1 A, 2.1.1 A | `<figure>` with `<figcaption>`, SVG `<desc>` summary, data table under "Show data table", legend or point links, Expand button and dialog; the card background opens the same dialog for mouse users only | Automated | `DashboardTests.Charts_are_accessible_figures…`, `…Charts_offer_a_maximize_button…`, `Chart_columns_and_bars_drill_through…`, `RedesignInteractionTests` | see Results |
| 20 | Accessible names never run words together | 4.1.2 A, 2.5.3 A | Hidden name suffixes written as `@($" {name}")` (Razor trims whitespace-only text nodes): "Remove clerk@example.test", "Download file.txt" | Automated | `UsersPageTests.Lists_users_with_roles_status…` | see Results |
| 21 | Screen readers NVDA and JAWS | 508 302.1 · 4.1.2 A, 4.1.3 AA | Semantic HTML, live regions, named dialogs | Manual | A11Y-SR-001 (NVDA), A11Y-SR-002 (JAWS) | **Not performed**: neither screen reader was available in this environment |
| 22 | QA test results page itself is accessible | E205.4 | Labelled "508 Accessibility" select, text badges, summary as a labelled list, polite count announcement, filters in the URL | Automated | `Section508FilterTests`, axe on the QA pages (`AccessibilitySweepTests`) | see Results |

## QA test results: how this matrix is recorded in the application

The application's QA test results module stores each test's accessibility classification, standards mapping and test
method separately from its result:

- Automated checks are classified only through explicit test traits (`A11y:Section508`, `A11y:Wcag22`,
  `Section508:<provision>`, `Wcag:<version>:<criterion>:<level>`, `Method:<method>`); nothing is inferred from a test's
  name or module, and tests recorded before this change stay unclassified.
- The manual checks above (A11Y-KB-001 to A11Y-VIS-003) are in `tests/accessibility/manual-test-cases.json`. Every run
  imports them; without a recorded result each one is **Not Run**. A recorded verdict needs a tester, an execution time
  and the observed result; a "Passed" without them is imported as Not Run and reported as a problem. To record a manual
  result, add a `result` object to the entry (outcome, executedAtUtc, tester, browser, assistiveTechnology, actual,
  defectReference, remediationNotes, evidence files placed next to the JSON) before the run.
- The "508 Accessibility" filter (All Tests, Section 508 Tests Only, Exclude Section 508 Tests) and the summary
  (Total, Passed, Failed, Blocked, Not Run, Not Applicable, pass rate = Passed ÷ (Passed + Failed), "N/A" when neither
  occurred) cover the whole filtered set on the server. The pass rate is not a conformance score.

## Results

Filled in from the recorded run of `scripts/run-all-tests.sh all` for this change; see
[../redesign-verification.md](../redesign-verification.md).
