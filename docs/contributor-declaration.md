# Contributor declaration

Required by RFQ E.3 (Q&A Q63). Lists **every** contributor, including people who do not present. Contributions were
derived from the Git history and merged pull requests, and each contributor confirms their own row.

All contributors are listed. Every contributor is presenting in the live demonstration.

## Contributors

| Name | Employer | Contribution | Before or after the live session | Git identities used | No OCI (RFQ B.2(5), Q23) |
|---|---|---|---|---|---|
| Rikhin Bhuta | World Services LLC | E4 integrations: veteran lookup mock service and resilient client, protected attachments (upload checks, protected storage, authorized download) (EPIC 4, `790c43e`); Government §4.6 contract served verbatim with conformance tests (PR #28) | Before | `Rikhin Bhuta <rbhuta@wsllc.com>`, `Rikhin Bhuta <rikhinb99@gmail.com>`, `rbhuta-wsllc <rbhuta@wsllc.com>` (GitHub web merge of PR #45) | Confirmed |
| Alhagie Bai Cham | World Services LLC | E1 platform: Docker Compose, PostgreSQL, migrations and seeding, logging, test harness (EPIC 1, `d4ab5ac`); JSON health checks (#14), pinned lock files and images (#17), test results pages (#11), multi-platform Docker (#26), five-role expansion (`a9aa564`, PR #20), user management and time zone (PR #30), architecture document (PR #34) | Before | `cham207388 <cham207388@gmail.com>` | Confirmed |
| Kabir Shaikh | World Services LLC | E2 case domain: model, validation, audit trail, list, status workflow, optimistic concurrency (EPIC 2, `1b9182e`); readiness fixes (PR #15), flaky-test fixes (PR #27), chart tooltip fix (PR #31), Amendment 0002 audit follow-up (PR #32), container image scan (PRs #33, #35); PostgreSQL Alpine database image without gosu (PR #36), absolute landing redirect (PR #37), E2E axe toast race (PR #38) | Before | `Kabir <kshaikh@wsllc.com>`, `therealkabirshaikh <kshaikh@wsllc.com>` | Confirmed |
| Dr. Janarthanan Senthurpandi | KAT Enterprises | E3 identity: sign-in, roles and permission matrix, server-side authorization, security headers and lockout (EPIC 3, `eb6a9fa`); tech-lead review (merged PRs #2 and #4, approved the D1 contract reversal); led the Amendment 0002 readiness audit (kept outside the repository); Change 1: database roles and permissions, system audit log, error log and System Health, Eastern time, Section 508 report (PR #42, `2907727`, issue #41) | Before | `Janarthanan Senthurpandi <jsenthurpandi@wsllc.com>`, `Janar07 <jsenthurpandi@wsllc.com>` | Confirmed |
| Antony Pon Jeyakumar Selvaraj | KAT Enterprises | E5 UI: Blazor UI (dashboard, list, intake, detail, edit and conflict UI, mobile), accessibility, Playwright and axe browser tests, test results module (PR #5, `b494ba5`); triage, assignment, dashboard and notifications (PR #29); UI redesign, Section 508 / WCAG 2.2 AA accessibility work and test matrix, NUnit 4.4 and Playwright 1.52 migration (ADR 0016), offline check (PR #44) | Before | `sajeyakumar <aselvaraj@wsllc.com>` | Confirmed |

"No OCI" confirms the contributor did no work on the Benefits & Memorial PMO order (MicroHealth LLC).

## AI tool use

All five contributors used Claude Code (Anthropic; model recorded in commit trailers as Claude Opus 5.5) as a coding
assistant. Each contributor ran their epic's implementation prompts from [Epic_Plan.md](Epic_Plan.md) (template:
[implementation-prompt.md](implementation-prompt.md)) in Claude Code to produce the initial build. Since then, every
contributor has used AI assistance to varying degrees in their pull requests: code, tests and documentation.

A named contributor reviewed, tested and committed every change, is accountable for it and can explain it.

Only some AI-assisted commits carry a `Co-Authored-By: Claude` trailer, so the table below is a lower bound and not a
complete list of AI-assisted work.

Commits with a `Co-Authored-By: Claude` trailer:

| Commit | Author |
|---|---|
| `2907727` | Dr. Janarthanan Senthurpandi |
| `0fe4a15` | Kabir Shaikh |
| `740bcaa` | Kabir Shaikh |
| `ccc738f` | Kabir Shaikh |
| `6a2b611` | Kabir Shaikh |
| `2be1711` | Kabir Shaikh |
| `57606d1` | Kabir Shaikh |
| `9a14e2a` | Kabir Shaikh |
| `e4cc48d` | Antony Pon Jeyakumar Selvaraj |
| `1cdafc0` | Kabir Shaikh |
| `10d9d85` | Kabir Shaikh |
| `6acbbb1` | Kabir Shaikh |
| `b494ba5` | Antony Pon Jeyakumar Selvaraj |
| `eb6a9fa` | Dr. Janarthanan Senthurpandi |

To regenerate the list:

```bash
git log --format='%h %an %(trailers:key=Co-Authored-By,valueonly,separator=;)' | grep -i claude
```

## Changes after the live session

Contributions made after the live session (the requirement change) are listed in
[change-implementation-record.md](change-implementation-record.md), with commit SHAs.
