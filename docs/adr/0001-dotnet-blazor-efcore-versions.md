# 0001. .NET 10 LTS, Blazor and EF Core versions

* Status: accepted
* Owner: E1

## Context

Specification 4.5 asks for a currently supported LTS runtime and for exact versions to be named. Blazor's shared
framework is not pinned by package versions alone.

## Decision

.NET 10 (LTS), C# 14, ASP.NET Core / Blazor 10, EF Core 10 with Npgsql 10. The SDK is pinned in `global.json`
(`rollForward: disable`), packages by Central Package Management with exact versions, `dotnet-ef` in
`dotnet-tools.json`, and the runtime/shared framework by the pinned image tag `sdk:10.0.402-noble`. Exact values
live in [versions.md](../versions.md) and a test fails if they drift.

## Alternatives considered

- .NET 8 LTS: older, fewer Blazor improvements; no reason to prefer it for a new build.
- Floating versions or `latest` tags: not reproducible.

## Tradeoffs

Exact pins mean security patches need a deliberate bump (documented in versions.md). `rollForward: disable` fails
loudly on a different SDK instead of silently using one.

## Consequences

Builds are reproducible in the container. Developers need SDK 10.0.402 locally.
