# Exact versions

`global.json` pins the SDK, `Directory.Packages.props` pins every NuGet package (Central Package Management),
`dotnet-tools.json` pins `dotnet-ef`, and the Dockerfiles pin the .NET image tags. The database image follows the
PostgreSQL 18 major-version tag (not yet pinned to a patch release). A test (`VersionConsistencyTests`) fails if the
tables below drift from those files, including any container image that is used but not listed here.

## SDK and runtime

| Component | Version |
|---|---|
| .NET SDK | 10.0.402 |
| .NET runtime (`Microsoft.NETCore.App`) | 10.0.13 |
| ASP.NET Core and Blazor shared framework (`Microsoft.AspNetCore.App`) | 10.0.13 |
| Entity Framework Core (`Microsoft.EntityFrameworkCore`) | 10.0.13 |

| Container image | Used by |
|---|---|
| `mcr.microsoft.com/dotnet/sdk:10.0.402-noble` | every compile step, and the `tests` service image (Ubuntu 24.04) |
| `mcr.microsoft.com/dotnet/aspnet:10.0.13-noble` | the running app: `web`, `migrator`, their e2e copies, and `veteran-lookup` (Ubuntu 24.04) |
| `postgres:18` | `db` |

All containers run as linux/amd64. Blazor ships in the ASP.NET Core shared framework, so its version is the
`Microsoft.AspNetCore.App` version above. The runtime versions were read from the built `web` and `veteran-lookup`
images with `dotnet --list-runtimes` (2026-10-19).

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

1. Change the version in `global.json` / `Directory.Packages.props` / `dotnet-tools.json` / the Dockerfiles
   (`Dockerfile`, `src/CaseIntake.VeteranLookup.Mock/Dockerfile`) / `docker-compose.yml`.
2. Update the tables above.
3. Run `docker compose run --rm tests`; the consistency test fails until the files agree.
