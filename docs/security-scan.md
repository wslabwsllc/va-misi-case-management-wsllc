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

Date: 2026-10-21. Images built from `6fecbca` (main) plus the Alpine database image
(`docker/db/Dockerfile`). Host: macOS arm64. Trivy 0.75.0, vulnerability DB updated 2026-10-20 19:12 UTC.
Severity HIGH and CRITICAL.

| Image | HIGH | CRITICAL |
|---|---|---|
| `caseintake-app:local` (web, migrator) | 0 | 0 |
| `caseintake-veteran-lookup` | 0 | 0 |
| `caseintake-db:local` (`docker/db/Dockerfile`: `postgres:18.6-alpine3.24` without `gosu`) | 0 | 0 |

Result: **PASS**. At every severity the database image has 2 MEDIUM findings and nothing higher.

### Why the database image is built here

The upstream PostgreSQL images all failed the scan (2026-10-20, images from `f36e2e6`):

| Image | OS packages (HIGH / CRITICAL) | `gosu` (HIGH / CRITICAL) |
|---|---|---|
| `postgres:18.6-trixie` (the previous pin) | 61 / 1 (libxml2 CVE-2026-6653), none fixable | 24 / 1 (CVE-2025-68121, Go `crypto/tls`) |
| `postgres:18.6-bookworm` | 71 / 3, none fixable | 24 / 1 |
| `postgres:18.6-alpine3.24` | 0 / 0 | 24 / 1 |

Every official image ships the same `gosu` build (Go stdlib v1.24.6), so none scans clean. `gosu` exists only so the
entrypoint can drop from root to the `postgres` user. `docker/db/Dockerfile` starts from the pinned Alpine image,
deletes `gosu` and sets `USER postgres`, so the entrypoint never needs it. The server binaries are unchanged, and
Testcontainers runs the same upstream image (`PostgresDatabaseFixture.PostgresImage`).

Alpine uses musl, whose default collation is byte order (`Banana` before `apple`). The cluster is therefore created
with ICU collation (`POSTGRES_INITDB_ARGS: "--locale-provider=icu --icu-locale=en-US"`, in Compose and the fixture),
which gives the same letter order as the previous glibc image. The one difference is punctuation, which now sorts
before letters and digits (`_note` first).
