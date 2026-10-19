# Architecture decision records

MADR-style records (context, decision, alternatives, tradeoffs, consequences). Copy [0000-template.md](0000-template.md).

| # | Title | Owner | Status |
|---|---|---|---|
| [0001](0001-dotnet-blazor-efcore-versions.md) | .NET 10 LTS, Blazor and EF Core versions | E1 | accepted |
| [0002](0002-data-access-and-database.md) | PostgreSQL 17 with EF Core and per-operation contexts | E1 | accepted |
| [0003](0003-telemetry-and-logging.md) | Structured JSON logs; no telemetry exporters | E1 | accepted |
| [0004](0004-test-strategy.md) | Test strategy and the `tests` service | E1 | accepted |
| [0005](0005-case-workflow.md) | Case status workflow as a domain-owned edge table | E2 | accepted |
| [0006](0006-audit-trail.md) | Audit information: audit fields plus an append-only trail | E2 | accepted |
| [0007](0007-roles-and-permissions.md) | Local Identity authentication, roles and the permission matrix | E3 | accepted |
| [0008](0008-veteran-lookup-mock-and-client.md) | Veteran Lookup: code-first mock, generated OpenAPI contract, resilient client | E4 | accepted |
| [0009](0009-attachment-storage.md) | Attachment storage and protection | E4 | accepted |

Still to be written by their owners (E1-S8 requires them before G3): render mode and accessibility approach (E5).
