# Accessibility Conformance Report: Case Intake and Triage

Structure: VPAT 2.5 (Section 508 edition). This is an engineering evidence report, not a signed vendor VPAT.

| Item | Value |
|---|---|
| Product | Case Intake and Triage (Blazor web application), revision recorded in [../verification.md](../verification.md) |
| Report date | 2026-10-21 |
| Standards | Revised Section 508 Standards (36 CFR 1194, Appendix C: Chapters 3, 5 and 6; web content via WCAG 2.0 A/AA, E205.4); WCAG 2.1 and 2.2 A/AA additions reported as well |
| Evaluation methods | Automated: axe-core 4.x in Chromium on every page, Playwright keyboard-only scenarios, focus-visibility and reflow sweeps, bUnit component tests (see [README.md](README.md)). Manual: procedures in [manual-test-procedures.md](manual-test-procedures.md), **not yet performed** |
| Browsers and assistive technology tested | Chromium (Playwright) at 320, 375, 390, 768, 1280 and 1440 px. No screen reader, magnifier or speech input has been tested yet |
| Contact | Tech lead (see [../contributor-declaration.md](../contributor-declaration.md)) |

## Terms

- **Supports**: the criterion is met and the automated evidence verifies it directly (or the content type does not occur and that is verified).
- **Partially Supports**: automated evidence covers part of the criterion; the rest needs the named manual check, or a known gap is described.
- **Does Not Support**: the majority of functionality does not meet the criterion.
- **Not Applicable**: the content the criterion is about does not exist in the product.
- **Not Evaluated**: the criterion needs human judgment or assistive technology and the manual check has not been done.

Manual checks are referenced as M1 to M9 ([manual-test-procedures.md](manual-test-procedures.md)).

## WCAG 2.x Level A

| Criterion | Conformance | Remarks and evidence |
|---|---|---|
| 1.1.1 Non-text Content | Partially Supports | Icons are decorative (`aria-hidden`) next to visible text or inside controls with accessible names; charts have a text description and a data table; axe image and name rules pass on every page. Quality of text alternatives needs M1 |
| 1.2.1 Audio-only and Video-only (Prerecorded) | Not Applicable | No audio or video content |
| 1.2.2 Captions (Prerecorded) | Not Applicable | No video |
| 1.2.3 Audio Description or Media Alternative | Not Applicable | No video |
| 1.3.1 Info and Relationships | Partially Supports | Headings, landmarks, labelled forms, fieldsets with legends for radio and checkbox groups, data tables with captions and `scope` headers (row group headers in the permission table); axe passes. Screen-reader reading needs M1 |
| 1.3.2 Meaningful Sequence | Partially Supports | DOM order follows visual order; keyboard sweep shows a logical Tab order. Needs M1 |
| 1.3.3 Sensory Characteristics | Supports | Instructions refer to labels and names, never to shape, position or colour alone |
| 1.4.1 Use of Color | Supports | Statuses, outcomes, health states and priorities are text plus colour (and an icon for health); links in text are underlined |
| 1.4.2 Audio Control | Not Applicable | No audio |
| 2.1.1 Keyboard | Partially Supports | Keyboard-only browser tests complete intake to closure, user and role administration, permission overrides, audit log filtering and export; all controls are native or have keyboard handlers. Native file picker and download prompt need M2 |
| 2.1.2 No Keyboard Trap | Supports | Focus sweep on every page leaves and re-enters the document; modal dialogs close with Esc and return focus (tested) |
| 2.1.4 Character Key Shortcuts (2.1) | Not Applicable | No single-character shortcuts |
| 2.2.1 Timing Adjustable | Partially Supports | No task has a time limit. The sign-in session ends after 8 hours without activity with no warning (an interactive page reconnects or asks to sign in again); see Known gaps |
| 2.2.2 Pause, Stop, Hide | Partially Supports | No auto-playing, blinking or scrolling content; status toasts are announcements that do not move content. Dashboard refresh behavior needs M3 |
| 2.3.1 Three Flashes or Below Threshold | Supports | No flashing content; animations are short transitions and are disabled under `prefers-reduced-motion` |
| 2.4.1 Bypass Blocks | Supports | "Skip to main content" is the first Tab stop on every page (sweep) and moves focus to `main` |
| 2.4.2 Page Titled | Supports | Every page has a specific title ending "- Case Intake and Triage" (sweep) |
| 2.4.3 Focus Order | Partially Supports | Focus moves to the new page's h1 after navigation, to error summaries after failed submits, into and back out of dialogs (tested). Logical order on every page needs M1 |
| 2.4.4 Link Purpose (In Context) | Partially Supports | Links have descriptive text or accessible names that include their visible text (for example "Entry A-12, 10-21-2026 09:05 ET"); axe passes. Needs M1 |
| 2.5.1 Pointer Gestures (2.1) | Supports | No path-based or multipoint gestures; the evidence viewer zooms with buttons |
| 2.5.2 Pointer Cancellation (2.1) | Supports | Actions use native click (up-event) activation |
| 2.5.3 Label in Name (2.1) | Partially Supports | Accessible names contain the visible label (axe `label-content-name-mismatch` passes). Needs M1 |
| 2.5.4 Motion Actuation (2.1) | Not Applicable | No motion-triggered functions |
| 3.1.1 Language of Page | Supports | `lang="en-US"` on every page (sweep) |
| 3.2.1 On Focus | Supports | No control changes context on focus |
| 3.2.2 On Input | Supports | Changing a field never submits or navigates; filters apply with an explicit button; the page-size select reloads the list in place and keeps focus |
| 3.2.6 Consistent Help (2.2) | Not Applicable | No help mechanism is provided on pages |
| 3.3.1 Error Identification | Supports | Field errors are text next to the field, linked with `aria-describedby`, `aria-invalid` set, listed in a focused error summary (bUnit and browser tests) |
| 3.3.2 Labels or Instructions | Supports | Every input has a programmatic label; required fields say "(required)"; hints are linked |
| 3.3.7 Redundant Entry (2.2) | Supports | No process asks for the same information twice |
| 4.1.1 Parsing | Supports | Obsolete in WCAG 2.2 (always satisfied); markup is framework-generated |
| 4.1.2 Name, Role, Value | Partially Supports | Native controls throughout; custom tabs, dialogs and radio groups use ARIA roles with names and states; axe passes on every page. Needs M1 with NVDA and VoiceOver |

## WCAG 2.x Level AA

| Criterion | Conformance | Remarks and evidence |
|---|---|---|
| 1.2.4 Captions (Live) | Not Applicable | No live media |
| 1.2.5 Audio Description (Prerecorded) | Not Applicable | No video |
| 1.3.4 Orientation (2.1) | Supports | Layout adapts to portrait and landscape; no orientation lock |
| 1.3.5 Identify Input Purpose (2.1) | Partially Supports | Sign-in and password fields use `autocomplete` (`username`, `current-password`, `new-password`); administrative email fields are `autocomplete="off"` because they describe another person. Needs M1 |
| 1.4.3 Contrast (Minimum) | Supports | Design tokens are chosen at 4.5:1 or more for text (documented in app.css); axe `color-contrast` passes on every page |
| 1.4.4 Resize Text | Partially Supports | Text uses relative sizing and reflows at 320 px; browser zoom to 200% needs M4 |
| 1.4.5 Images of Text | Supports | No images of text |
| 1.4.10 Reflow (2.1) | Supports | No page scrolls sideways at 320 CSS px (sweep); wide data tables scroll inside their card, which the criterion allows for data tables |
| 1.4.11 Non-text Contrast (2.1) | Partially Supports | Borders, focus rings and states use tokens at 3:1 or more; chart colours were validated in the palette. Needs M5 |
| 1.4.12 Text Spacing (2.1) | Not Evaluated | Needs M4 (text-spacing bookmarklet) |
| 1.4.13 Content on Hover or Focus (2.1) | Partially Supports | Chart tooltips appear on hover and focus, are dismissible with Esc and stay while hovered. Needs M3 |
| 2.4.5 Multiple Ways | Supports | Navigation menu, breadcrumbs, dashboard drill-through links and search on every list |
| 2.4.6 Headings and Labels | Partially Supports | One h1 per page (sweep), sections have h2 headings, labels describe purpose. Needs M1 |
| 2.4.7 Focus Visible | Supports | Every focused element on every page has a visible indicator on itself, its card or its chart shape (sweep, 30 Tab stops per page) |
| 2.4.11 Focus Not Obscured (Minimum) (2.2) | Partially Supports | Scroll padding keeps a control that Tab scrolls into view clear of the sticky header and the phone bottom bar; the focus sweep fails if a focused control is under the header on any page. Phone bottom bar needs M6 |
| 2.5.7 Dragging Movements (2.2) | Not Applicable | No dragging |
| 2.5.8 Target Size (Minimum) (2.2) | Partially Supports | Phone navigation targets are at least 44x44 px (tested); desktop controls are at least 24x24 px by design (checkboxes 24 px, radio 20 px with label text as part of the target). Needs M6 |
| 3.1.2 Language of Parts | Not Applicable | All content is English |
| 3.2.3 Consistent Navigation | Supports | One site map drives the sidebar, bottom bar and More sheet (`AppNavigation`) |
| 3.2.4 Consistent Identification | Supports | Shared components (pager, filters, error summary, dialogs) are used everywhere |
| 3.3.3 Error Suggestion | Supports | Messages say how to fix the input ("Enter a valid email address, like name@example.test", "Use at most 64 characters") |
| 3.3.4 Error Prevention (Legal, Financial, Data) | Supports | Destructive or access-changing actions (deactivate user, delete role, status changes) ask for confirmation; concurrent edits are detected and nothing is overwritten |
| 3.3.8 Accessible Authentication (Minimum) (2.2) | Supports | Sign-in is email and password with paste and password managers allowed (`autocomplete`); no cognitive test |
| 4.1.3 Status Messages (2.1) | Partially Supports | Saves, results counts and refreshes are announced through a polite live region; errors use `role="alert"` (bUnit verifies the regions). Hearing them needs M1 |

## Revised Section 508 Chapter 3: Functional Performance Criteria

| Criterion | Conformance | Remarks |
|---|---|---|
| 302.1 Without Vision | Not Evaluated | Needs M1 (NVDA, VoiceOver) |
| 302.2 With Limited Vision | Partially Supports | Reflow at 320 px and contrast verified; zoom and magnifier need M4 |
| 302.3 Without Perception of Color | Supports | No information by colour alone (1.4.1) |
| 302.4 Without Hearing | Supports | No audio |
| 302.5 With Limited Hearing | Supports | No audio |
| 302.6 Without Speech | Supports | No speech input required |
| 302.7 With Limited Manipulation | Partially Supports | Keyboard-only operation verified for the main and administrative tasks; switch and voice control need M7 |
| 302.8 With Limited Reach and Strength | Partially Supports | No timed or simultaneous actions; target sizes as 2.5.8 |
| 302.9 With Limited Language, Cognitive, and Learning Abilities | Partially Supports | Plain-language labels and messages, consistent layout, confirmations; needs M8 review |

## Chapter 5: Software

| Criterion | Conformance | Remarks |
|---|---|---|
| 501.1 Scope (web content) | Not Applicable | The product is web content evaluated under WCAG (E205.4); it is not platform software |
| 502 Interoperability with Assistive Technology | Not Evaluated | Browser-hosted; exposes standard HTML semantics. Needs M1 |
| 503 Applications | Not Applicable | Web application (WCAG applies) |
| 504 Authoring Tools | Not Applicable | Users enter case text but do not author web content |

## Chapter 6: Support Documentation and Services

| Criterion | Conformance | Remarks |
|---|---|---|
| 602.2 Accessibility and Compatibility Features | Supports | README, [../accessibility-review.md](../accessibility-review.md) and this folder describe keyboard use, skip link, focus behavior and supported layouts |
| 602.3 Electronic Support Documentation | Partially Supports | Documentation is Markdown with headings, tables and text alternatives; rendered output not checked against WCAG |
| 602.4 Alternate Formats for Non-Electronic Support Documentation | Not Applicable | No non-electronic documentation |
| 603 Support Services | Not Applicable | No support services are provided with the evaluation deliverable |

## Known gaps

- The manual procedures M1 to M9 have not been performed; every "Not Evaluated" and the manual part of every
  "Partially Supports" depends on them.
- Session timeout: an inactive session ends after 8 hours without warning (2.2.1). Typing in a form does not keep the
  session alive without a server round trip.
- Native date inputs are rendered by the browser in its own locale and format.
- Charts' individual bars are reachable by keyboard as links; their exact values are in the data table under each chart.
