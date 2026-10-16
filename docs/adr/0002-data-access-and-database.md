# 0002. PostgreSQL 17 with EF Core and per-operation contexts

* Status: accepted
* Owner: E1

## Context

Interactive Server DI scopes can last as long as a circuit. A retained `DbContext` would be shared by concurrent
component operations and would hold stale tracked state. The spec leaves the engine open (OQ-14).

## Decision

PostgreSQL 17 (container, named volume) through EF Core 10 and Npgsql. `AppDbContext` is registered only through
`IDbContextFactory<AppDbContext>`; every operation creates and disposes its own context. Queries return DTOs. Npgsql
retry-on-failure is enabled, so explicit transactions run inside the provider execution strategy. Sensitive data
logging is off. `Timezone=UTC` is set on the connection. A one-shot `migrator` service applies migrations (with
backoff for up to 60 s) and runs seed contributors; `web` starts only after it succeeds.

## Alternatives considered

- SQL Server: heavier image and licensing; works but offers no benefit here.
- SQLite: no concurrency token (`xmin`) and not representative of a server database.
- A scoped context per circuit: rejected for the lifetime reasons above.

## Tradeoffs

Slightly more ceremony per operation in exchange for correct concurrency and disposal.

## Consequences

Feature modules must inject the factory, never the context. E2 audit writes share one context/transaction with the
mutation. Migrations are reviewed by E1, one per PR.
