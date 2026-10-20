# Contributor declaration

Required by RFQ E.3 (Q&A Q63). Lists **every** contributor, including people who do not present. Each contributor
fills in their own row and confirms it. Contributions below were drafted from the Git history; fields marked _TBD_
are not yet confirmed.

## Contributors

| Name | Employer | Contribution | Before or after the live session | Git identities used | No OCI (RFQ B.2(5), Q23) |
|---|---|---|---|---|---|
| Rikhin Bhuta | _TBD_ | E4 integrations: veteran lookup client and mock, attachments, Government contract conformance (US-4.1 / R12) | Before | `Rikhin Bhuta <rbhuta@wsllc.com>`, `Rikhin Bhuta <rikhinb99@gmail.com>` | _TBD_ |
| Janarthanan Senthurpandi | _TBD_ | E3 identity, roles and authorization | Before | `Janarthanan Senthurpandi <jsenthurpandi@wsllc.com>`, `Janar07 <jsenthurpandi@wsllc.com>` | _TBD_ |
| _TBD_ | _TBD_ | E5 UI and accessibility; dashboard, triage and assignment (issues #21/#22) | Before | `sajeyakumar <aselvaraj@wsllc.com>` | _TBD_ |
| _TBD_ | _TBD_ | E1 platform and delivery pipeline; roles, health checks, multi-platform Docker, user management | Before | `cham207388 <cham207388@gmail.com>` | _TBD_ |
| _TBD_ | _TBD_ | E2 case domain; readiness fixes, flaky-test fixes, chart tooltips | Before | `Kabir <kshaikh@wsllc.com>`, `therealkabirshaikh <kshaikh@wsllc.com>` | _TBD_ |

"No OCI" confirms the contributor did no work on the Benefits & Memorial PMO order (MicroHealth LLC).

## AI tool use

Claude Code (Anthropic; model recorded in commit trailers as Claude Opus 5.5) was used as a coding assistant. Commits
it helped write carry a `Co-Authored-By: Claude` trailer. A named contributor reviewed, tested and committed every
change and is accountable for it.

| Commit | Author |
|---|---|
| `eb6a9fa` | Janarthanan Senthurpandi |
| `b494ba5` | sajeyakumar |
| `6acbbb1` | Kabir |
| `10d9d85` | Kabir |
| `1cdafc0` | Kabir |
| `e4cc48d` | sajeyakumar |
| `9a14e2a` | Kabir |

To regenerate the list:

```bash
git log --format='%h %an %(trailers:key=Co-Authored-By,valueonly,separator=;)' | grep -i claude
```

## Changes after the live session

Contributions made after the live session (the requirement change) are listed in
[change-implementation-record.md](change-implementation-record.md), with commit SHAs.
