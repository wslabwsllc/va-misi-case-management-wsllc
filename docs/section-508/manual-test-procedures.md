# Manual accessibility test procedures

Checks automation cannot do. Run them on the revision under review with the Compose stack (`docker compose up --build`,
`http://localhost:8080`, demo accounts in the README). Record each result in the sign-off table at the end; a check is
done only when it was actually performed. Report a failure as a GitHub issue with the page, steps, assistive technology
and version, and link it in the table.

Pages to cover in every check unless the check says otherwise: sign-in, dashboard, case list, new case (with errors),
case detail (edit, status change dialog, attachments, audit trail), Users, a user's page, Roles, role editor, Audit log,
an audit entry, Errors, an error, System health, change password, access denied, Not Found.

## M1 Screen readers (WCAG 1.1.1, 1.3.1, 1.3.2, 2.4.3, 2.4.4, 2.4.6, 2.5.3, 4.1.2, 4.1.3; 508 302.1, 502)

Run twice: NVDA (current) with Chrome on Windows, and VoiceOver with Safari on macOS.

1. Sign in with the screen reader only. Expected: field names, "required", the error message after a wrong password.
2. On each page, list headings (NVDA: Insert+F7; VoiceOver: rotor). Expected: one h1 naming the page, h2 per section.
3. List landmarks. Expected: banner, navigation (main menu, breadcrumb), main.
4. Read the case list table with table navigation. Expected: column headers read with each cell; caption announces the range.
5. New case: submit empty. Expected: the error summary is read and focused; each link moves to its field; the field reads its error.
6. Users: add a user with two roles. Expected: "Roles, required" group with each role and its description; the
   success message is announced without moving focus.
7. A user's page: change an override with the arrow keys. Expected: "Override for Edit cases, radio group", choices
   announced with checked state; "Permissions saved." announced after Save.
8. Deactivate the user. Expected: dialog title and description read on open; focus starts on Cancel; Esc closes and
   returns to "Deactivate user".
9. Audit log: apply a filter. Expected: the result count is announced; the entry link names include the time.
10. System health: "Check again". Expected: "Checked again. Overall: …" is announced.

## M2 Keyboard with native browser UI (WCAG 2.1.1)

1. Case detail, Attachments: Tab to the file input, Space or Enter opens the native picker, choose a file with the
   keyboard, Upload. Expected: upload succeeds, focus stays sensible.
2. Download an attachment and an audit CSV with Enter. Expected: the browser's download UI works by keyboard.

## M3 Moving, timed and hover content (WCAG 1.4.13, 2.2.1, 2.2.2)

1. Dashboard: leave it open 10 minutes. Expected: any refresh does not move focus or the reading position.
2. Hover and focus chart bars. Expected: tooltip stays while hovered, Esc hides it, it does not cover the focused bar.
3. Note the session timeout (8 hours inactive) in the result; no task has its own time limit.

## M4 Zoom, text resize and text spacing (WCAG 1.4.4, 1.4.12; 508 302.2)

1. Browser zoom 200% and 400% at 1280 px wide on each page. Expected: no loss of content or function; no sideways
   scrolling except inside data tables.
2. Text-only zoom (Firefox "Zoom text only") to 200%. Expected: nothing clipped or overlapping.
3. Apply the WCAG text-spacing bookmarklet (line height 1.5, paragraph spacing 2, letter spacing 0.12, word spacing
   0.16). Expected: no clipped or overlapping text.

## M5 Contrast of non-text content and Windows high contrast (WCAG 1.4.11)

1. Measure focus rings, input borders, checkbox and radio outlines, chart bars against their backgrounds. Expected: 3:1 or more.
2. Windows High Contrast (forced colors) on. Expected: focus, borders, status pills, chart bars and selected states remain visible.

## M6 Focus not obscured and target size (WCAG 2.4.11, 2.5.8)

1. On long pages (case list, user detail permission table, audit log) Tab through at 1280x720 and at 390x844. Expected:
   no focused control is fully hidden by the sticky header or the phone bottom bar.
2. Measure small controls (chip remove buttons, pager buttons, radio buttons with labels). Expected: target at least
   24x24 CSS px or enough spacing.

## M7 Voice and switch control (508 302.7)

1. Voice Control (macOS) or Dragon: "Click Users", "Click Save permissions", select a checkbox by its label. Expected:
   every control can be addressed by its visible label.

## M8 Plain language and error recovery review (508 302.9)

1. A reviewer not on the team reads the Users, Roles, user detail and Errors pages. Expected: labels, hints, read-only
   reasons and error messages are understood without help.

## M9 Real devices (WCAG 1.3.4, 1.4.10; responsive)

1. iOS Safari and Android Chrome: sign in, open a case, change status, open Users and the audit log, rotate the
   device. Expected: bottom navigation reachable, safe areas respected, keyboard types right for email and dates.

## Sign-off

| Check | Tester | Date | AT, browser, OS (versions) | Result | Issues |
|---|---|---|---|---|---|
| M1 NVDA + Chrome | | | | Not yet performed | |
| M1 VoiceOver + Safari | | | | Not yet performed | |
| M2 | Rikhin Bhuta | 2026-10-22 | Chrome, macOS 26.2 | Partially performed. Pass: step 1 (native file picker opened with the keyboard, file chosen, Upload) and the attachment Download with Enter. Not yet run: audit CSV download with Enter (step 2) | None found |
| M3 | Kabir Shaikh (reviewer sign-off pending) | 2026-10-22 | Chromium 152, macOS 26.6.2; `main` @ `a43e242` | Performed. Pass: step 1 (dashboard open 10 minutes with focus on a chart bar: no DOM changes, focus and scroll position unchanged); step 2 (chart tooltip shown on hover and on keyboard focus, stays while the pointer is over it, does not cover the focused bar, Esc hides it and focus stays; icon-button tooltips also hide with Esc); step 3: session timeout is 8 hours inactive (`IdentityModule`), no task has its own time limit. Note: after Esc with the pointer still over the chart, the tooltip of the column now under the pointer shows | None found |
| M4 | Kabir Shaikh (reviewer sign-off pending) | 2026-10-22 | Chromium 152, macOS 26.6.2, zoom levels emulated as CSS viewports (200% = 640x360, 400% = 320x180 for a 1280x720 window); `main` @ `a43e242` | Partially performed. Step 1: no sideways scrolling at 640x360 on dashboard, case list, new case, case detail, Users, user detail (all 22 pages at 320 px wide are covered by the automated sweep). At 320x180 the sticky header (56 px) and bottom bar (61 px) leave 63 px for content; the floating "New case" button covers about half of each focused case title on the case list (no control fully hidden). Step 3 fail: with the text-spacing overrides at 1280x1024, six legend labels of "Cases by status" and "Open cases by priority" on the dashboard run 3 to 24 px into their counts ("Resolved" into "12", "Medium" into "21"). No clipped text on case list, case detail, user detail, audit log (case list descriptions are clamped to two lines with or without the overrides; full text on the case page). Not yet run: step 2 (Firefox text-only zoom), step 1 on the remaining pages | Not yet filed: dashboard legend labels overlap their counts with text spacing; header, bottom bar and floating button take most of the height at 400% zoom |
| M5 | Kabir Shaikh (reviewer sign-off pending) | 2026-10-22 | Chromium 152, macOS 26.6.2; computed colours; `main` @ `a43e242` | Partially performed. Step 1 pass: focus outline on buttons 3 px #101828 (16.5:1 on white), on checkboxes and radios 2 px #315cf5 with a 4 px halo (5.3:1); input borders #8b93a5 (3.08:1 on white); checkboxes and radios native with the default border; chart fills 3.3:1 to 17.8:1 on white. Not yet run: step 2 (Windows high contrast) | None found |
| M6 | Rikhin Bhuta (reviewed); run with Claude Code through the Claude in Chrome extension | 2026-10-22 | Chrome 151, macOS 26.2; real Tab key presses, focused-control visibility and target sizes measured by script after each scroll settled; `main` @ `10125df` | Partially performed. Pass: no focused control fully hidden on case list (45 stops, 1512x771), user detail permission table (45 stops, 1280x720), audit log (39 stops, 1280x720; 32 stops, 500x772 phone layout with bottom bar). Targets under 24 px pass by label or spacing: checkboxes and radios 20x20 with clickable labels at least 44 px tall and at least 48 px apart; 16 px table links at least 47 px apart. Not yet run: case list at 1280x720, case list and user detail in the phone layout, exact 390x844 (Chrome's minimum window width is 500 px; needs DevTools device mode) | None found |
| M6 (remaining) | Kabir Shaikh (reviewer sign-off pending) | 2026-10-22 | Chromium 152 with viewport emulation, macOS 26.6.2; real Tab key presses, visible share of each focused control sampled with `elementFromPoint`; `main` @ `a43e242` | Performed. Fail at 390x844 on the case list (54 stops): Tab to the "Due from" filter's "Choose date from calendar" button puts it entirely behind the floating "New case" button (0 of 110 sample points visible) and the page does not scroll. Pass at 390x844: user detail including the permission table (60 stops), audit log (60 stops). Case list at 1280x720 (80 stops): no control fully hidden, but after an "Actions for case N" button gets focus the table's scroll area stays scrolled right, and the next row's title link is only 14-29% visible when focused (8 of about 18 rows; the row outline stays visible) | Not yet filed: calendar button hidden behind the floating "New case" button at 390x844; case table does not scroll a focused title link back into view |
| M7 | | | | Not yet performed | |
| M8 | | | | Not yet performed | |
| M9 | | | | Not yet performed | |
