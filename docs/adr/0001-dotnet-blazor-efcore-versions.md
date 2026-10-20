# 0001. .NET 10 LTS, Blazor and EF Core versions

* Status: accepted
* Owner: E1

## Context

Specification 4.5 asks for a currently supported LTS runtime and for exact versions to be named. Blazor's shared
framework is not pinned by package versions alone. Part 2 Q112 (Amendment 0002) adds the Government's rationale:
.NET 8 support ends on 2026-11-10 and the Army is moving EISS to .NET 10.

## Decision

.NET 10 (LTS), C# 14, ASP.NET Core / Blazor 10, EF Core 10 with Npgsql 10. The SDK is pinned in `global.json`
(`rollForward: disable`), packages by Central Package Management with exact versions, `dotnet-ef` in
`dotnet-tools.json`, and the runtime/shared framework by the pinned image tags `sdk:10.0.402-noble` and
`aspnet:10.0.13-noble`. Exact values live in [versions.md](../versions.md) and a test fails if they drift.

Tags alone can be re-pushed (Microsoft republishes .NET tags for OS patches), so a rebuild weeks later could still
change. Therefore every image (.NET and PostgreSQL) is also pinned by digest, every project commits a
`packages.lock.json` that the Docker builds restore in locked mode (versions and content hashes of the whole package
graph), and apt reads a dated `snapshot.ubuntu.com` archive, which also fixes the OS packages Playwright installs for
Chromium.

## Alternatives considered

- .NET 8 LTS: older, fewer Blazor improvements; no reason to prefer it for a new build.
- Floating versions or `latest` tags: not reproducible.

## Tradeoffs

Exact pins mean security patches need a deliberate bump (documented in versions.md): OS and image patches arrive only
when someone updates the digests and the apt snapshot date. `rollForward: disable` fails
loudly on a different SDK instead of silently using one.

## Consequences

Builds are reproducible in the container. Developers need SDK 10.0.402 locally.
