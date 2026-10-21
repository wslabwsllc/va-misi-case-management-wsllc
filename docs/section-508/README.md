# Section 508 evidence

Evidence for the accessibility of Case Intake and Triage (Change 1, part 5). Section 508 (36 CFR 1194, Revised 508
Standards) incorporates WCAG 2.0 Level A and AA for web content; this set also reports the WCAG 2.1 and 2.2 AA additions
because the project's yardstick is WCAG 2.2 AA (assumption A10).

| Document | What it is |
|---|---|
| [accessibility-conformance-report.md](accessibility-conformance-report.md) | Conformance report in the VPAT 2.5 (Section 508 edition) structure: each criterion, its conformance level and the evidence |
| [manual-test-procedures.md](manual-test-procedures.md) | The manual checks automation cannot do (screen readers, zoom, high contrast, real devices), with steps, expected results and a sign-off table |
| [../accessibility-review.md](../accessibility-review.md) | The running accessibility review record (automated evidence and manual status) |

## How the evidence is produced

All automated evidence comes from `docker compose run --rm tests` (or the same projects run on a host, see
[../verification.md](../verification.md)); browser runs store screenshots, traces and axe results under
`test-results/<run>/evidence/`, viewable in the app under Administration > Quality assurance.

| Automated check | Test | Covers |
|---|---|---|
| axe-core, WCAG 2.0/2.1/2.2 A and AA rules, every page | `AccessibilitySweepTests.Every_page_passes_axe_and_has_one_h1_a_title_a_language_and_a_main_landmark`, `Public_pages_pass_axe_and_have_the_same_structure` | 22 signed-in pages (all of them, as Administrator) and 3 public pages |
| axe on dialogs, forms with errors, conflicts, phone layouts | `PrimaryWorkflowTests`, `RolesAndPermissionsTests`, `AuditLogTests`, `ResponsiveLayoutTests`, `TestResultsModuleTests` | states the page sweep does not reach |
| Page structure | the two sweep tests above | one h1, page-specific title, `lang="en-US"`, `main` landmark |
| Keyboard: bypass block, focus visible, no focus loss | `AccessibilitySweepTests.On_every_page_the_skip_link_comes_first_and_keyboard_focus_stays_visible` | every signed-in page, 30 Tab stops each |
| Keyboard-only task completion | `PrimaryWorkflowTests` (intake to closure), `RolesAndPermissionsTests`, `AuditLogTests`, `UserAdministrationTests` | the main workflow and administration, including dialogs (Esc, focus return) |
| Reflow at 320 CSS px (400% zoom) | `AccessibilitySweepTests.Every_page_reflows_at_320_css_pixels_without_sideways_scrolling` | every signed-in page |
| Touch target size, phone layouts | `ResponsiveLayoutTests` | bottom navigation >= 44x44 px at 375 and 390 px |
| Labels, descriptions, live regions, focus calls | `CaseIntake.Web.Tests` (bUnit) | every form and page component |
| Time and language formats | `DateTimeFormatTests`, `AccessibilitySweepTests.Times_are_shown_in_eastern_time_with_the_zone_in_en_US_format` | en-US, Eastern, labelled ET |

## Status

Automated checks pass on the revision recorded in [../verification.md](../verification.md). The manual procedures have
**not been performed yet**; until they are signed off, the report lists the criteria that depend on them as
"Not Evaluated" and makes no full-conformance claim.
