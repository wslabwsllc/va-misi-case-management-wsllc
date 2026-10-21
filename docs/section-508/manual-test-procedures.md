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
| M2 | | | | Not yet performed | |
| M3 | | | | Not yet performed | |
| M4 | | | | Not yet performed | |
| M5 | | | | Not yet performed | |
| M6 | | | | Not yet performed | |
| M7 | | | | Not yet performed | |
| M8 | | | | Not yet performed | |
| M9 | | | | Not yet performed | |
