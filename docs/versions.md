# Exact versions

Every runtime, tooling, image and OS package version is pinned, so a rebuild weeks after the demo produces the same
build even after new upstream releases. `global.json` pins the SDK, `Directory.Packages.props` pins every NuGet
package (Central Package Management), each project's `packages.lock.json` locks the whole package graph with content
hashes (the Docker builds restore with `--locked-mode`), `dotnet-tools.json` pins `dotnet-ef`, every container image
is pinned by tag and digest, and apt reads a dated Ubuntu snapshot. A test (`VersionConsistencyTests`) fails if the
tables below drift from those files.

## SDK and runtime

| Component | Version |
|---|---|
| .NET SDK | 10.0.402 |
| ASP.NET Core runtime | 10.0.13 |
| PostgreSQL | 18.6 |

The runtime and ASP.NET Core / Blazor shared-framework versions come from the pinned images below. The exact
inventory is recorded from the built image in [verification.md](verification.md) (output of `dotnet --list-runtimes`).

## Container images

Pinned by tag (readable) and digest (immutable). The digest is the multi-platform index, so the SDK build stages can
run natively on the build machine while the final images are linux/amd64.

| Use | Image |
|---|---|
| SDK build stages and test image | `mcr.microsoft.com/dotnet/sdk:10.0.402-noble@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa` |
| App and lookup mock runtime | `mcr.microsoft.com/dotnet/aspnet:10.0.13-noble@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac` |
| Database (Compose `db`, Testcontainers) | `postgres:18.6-trixie@sha256:74935e72241653ca55e0414067e6d8763aceb8a810eb51b452253ec3dcfc4336` |

## OS packages

| Source | Pin |
|---|---|
| Ubuntu apt snapshot (`APT_SNAPSHOT` in `Dockerfile`) | 20261016T000000Z |

`libgssapi-krb5-2` (runtime image) and the Chromium dependencies that `playwright install --with-deps` adds to the
test image come from `snapshot.ubuntu.com` at that timestamp. The Chromium build itself is fixed by
`Microsoft.Playwright`.

## NuGet packages (runtime)

| Package | Version |
|---|---|
| `Microsoft.EntityFrameworkCore` | 10.0.13 |
| `Microsoft.EntityFrameworkCore.Design` | 10.0.13 |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | 10.0.13 |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.3 |
| `Npgsql` | 10.0.3 |
| `Microsoft.Extensions.Logging.Console` | 10.0.13 |
| `Microsoft.Extensions.Compliance.Redaction` | 10.11.0 |
| `Microsoft.AspNetCore.OpenApi` | 10.0.13 |
| `Microsoft.Extensions.Http.Resilience` | 10.11.0 |

## Build and test tooling

| Package | Version |
|---|---|
| `Microsoft.CodeAnalysis.BannedApiAnalyzers` | 5.6.0 |
| `Microsoft.NET.Test.Sdk` | 18.11.0 |
| `xunit` | 2.9.3 |
| `xunit.runner.visualstudio` | 3.1.5 |
| `Microsoft.AspNetCore.Mvc.Testing` | 10.0.13 |
| `Microsoft.Extensions.TimeProvider.Testing` | 10.11.0 |
| `Microsoft.Extensions.Diagnostics.Testing` | 10.11.0 |
| `Testcontainers.PostgreSql` | 4.16.0 |
| `Respawn` | 7.0.0 |
| `NetArchTest.Rules` | 1.3.2 |
| `bunit` | 2.11.3 |
| `Microsoft.Playwright` | 1.63.0 |
| `Deque.AxeCore.Playwright` | 4.13.0 |

Tool: `dotnet-ef` 10.0.13 (see `dotnet-tools.json`).

## Updating

1. Change the version in `global.json` / `Directory.Packages.props` / `dotnet-tools.json`, then run
   `dotnet restore CaseIntake.sln --force-evaluate` and commit the changed `packages.lock.json` files.
2. For an image, look up the new digest with `docker buildx imagetools inspect <image>:<tag>` (the `Digest:` line of
   the index) and update every reference: `Dockerfile`, `src/CaseIntake.VeteranLookup.Mock/Dockerfile`,
   `docker-compose.yml`, `PostgresDatabaseFixture.PostgresImage` and the README.
3. For OS patches, move `APT_SNAPSHOT` to a newer timestamp (not older than the base images' build date).
4. Update the tables above.
5. Run `docker compose run --rm tests`; the consistency test fails until the files agree.
