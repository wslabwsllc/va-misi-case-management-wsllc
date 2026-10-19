# Exact versions

Every runtime and tooling version is pinned. `global.json` pins the SDK, `Directory.Packages.props` pins every
NuGet package (Central Package Management), `dotnet-tools.json` pins `dotnet-ef`, and the Dockerfile pins the image
tag. A test (`VersionConsistencyTests`) fails if the tables below drift from those files.

## SDK and runtime

| Component | Version |
|---|---|
| .NET SDK | 10.0.402 |
| Base image | `mcr.microsoft.com/dotnet/sdk:10.0.402-noble` (Ubuntu 24.04, linux/amd64) |
| Database image | `postgres:17` |

The runtime and ASP.NET Core / Blazor shared-framework versions come from that SDK image. The exact inventory
is recorded from the built image in [verification.md](verification.md) (output of `dotnet --list-runtimes`).

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

1. Change the version in `global.json` / `Directory.Packages.props` / `dotnet-tools.json` / `Dockerfile`.
2. Update the tables above.
3. Run `docker compose run --rm tests`; the consistency test fails until the files agree.
