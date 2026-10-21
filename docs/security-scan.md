# Dependency vulnerability scan

## NuGet packages

Date: 2026-10-20. Revision: `9672596` (main). Host: macOS arm64, .NET SDK 10.0.402. Source: nuget.org (GitHub
Advisory Database).

```bash
dotnet list CaseIntake.sln package --vulnerable --include-transitive
```

Result: **no vulnerable packages** in any of the 20 projects (12 `src`, 8 `tests`), direct or transitive.

Re-run: 2026-10-21 on branch `feat/redesign-and-508-results` (base `06f899b`, main), Windows Server 2025 x64, .NET SDK 10.0.402, same command. Result: **no vulnerable packages** in all 20 projects,
direct or transitive.

## Container images

`scripts/scan-images.sh` runs Trivy (pinned in [versions.md](versions.md)) in Docker against the images
`docker-compose.yml` runs; `--all` adds the test image. It fails on any HIGH or CRITICAL finding not listed in
`.trivyignore`, and writes reports to `artifacts/security-scan/`.

```bash
docker compose build
scripts/scan-images.sh
```

Date: 2026-10-20. Images built from `f36e2e6` (main). Host: macOS arm64. Trivy 0.75.0, vulnerability DB updated
2026-10-20 19:12 UTC. Severity HIGH and CRITICAL.

| Image | HIGH | CRITICAL | Fix available |
|---|---|---|---|
| `caseintake-app:local` (web, migrator) | 0 | 0 | - |
| `caseintake-veteran-lookup` | 0 | 0 | - |
| `postgres:18.6-trixie` Debian 13.7 packages (libxml2, util-linux, login, ...) | 61 | 1 (libxml2 CVE-2026-6653) | none: 57 affected, 4 fix deferred by Debian |
| `postgres:18.6-trixie` `/usr/local/bin/gosu` (Go stdlib v1.24.6) | 24 | 1 (CVE-2025-68121, crypto/tls) | yes, in Go 1.24.13 or later |

Result: **FAIL** on the database image; both application images are clean. With `TRIVY_IGNORE_UNFIXED=1` only the
25 `gosu` findings remain. The pinned digest is the newest `18.6-trixie` build, so re-pinning the same tag changes
nothing.

Other official 18.6 variants, scanned the same way for comparison:

| Image | OS packages (HIGH / CRITICAL) | `gosu` (HIGH / CRITICAL) |
|---|---|---|
| `postgres:18.6-bookworm` | 71 / 3, none fixable | 24 / 1 |
| `postgres:18.6-alpine3.24` | 0 / 0 | 24 / 1 |

Every official image ships the same `gosu` build, so none scans clean.

Assessment, pending the lead's decision (the pin is unchanged):

- The findings are in the upstream PostgreSQL image, not in code this project builds. The database is reachable only
  on the Compose network (no published port) and holds synthetic data only.
- `gosu` runs once in the image's entrypoint to drop from root to the `postgres` user and then hands over to
  PostgreSQL; it makes no TLS or network calls, so the Go `crypto/tls` and `net/http` findings are not reachable.
- The Debian findings have no fixed package yet; a newer `18.x-trixie` build picks them up when Debian ships fixes.
- Options: (1) keep the pin and list the findings in `.trivyignore` with this reasoning; (2) switch to
  `postgres:18.6-alpine3.24`, which clears the OS findings but changes the C library (default text collation, so the
  case list's Title sort order may change) and needs empty volumes, the Testcontainers pin and a full suite run;
  (3) build on Alpine without `gosu` and run as the `postgres` user, which scans clean but adds an image to maintain.
