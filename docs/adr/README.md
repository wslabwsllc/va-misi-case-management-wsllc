# Architecture decision records

MADR-style records (context, decision, alternatives, tradeoffs, consequences). Copy [0000-template.md](0000-template.md).

| # | Title | Owner | Status |
|---|---|---|---|
| [0001](0001-dotnet-blazor-efcore-versions.md) | .NET 10 LTS, Blazor and EF Core versions | E1 | accepted |
| [0002](0002-data-access-and-database.md) | PostgreSQL 17 with EF Core and per-operation contexts | E1 | accepted |
| [0003](0003-telemetry-and-logging.md) | Structured JSON logs; no telemetry exporters | E1 | accepted |
| [0004](0004-test-strategy.md) | Test strategy and the `tests` service | E1 | accepted |

Still to be written by their owners (E1-S8 requires them before G3): render mode and accessibility approach (E5),
auth and roles (E3), workflow design and audit approach (E2), attachment storage and veteran lookup mock/client (E4).
