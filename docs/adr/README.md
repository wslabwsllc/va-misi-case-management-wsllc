# Architecture decision records

MADR-style records (context, decision, alternatives, tradeoffs, consequences). Copy [0000-template.md](0000-template.md).

| # | Title | Owner | Status |
|---|---|---|---|
| [0001](0001-dotnet-blazor-efcore-versions.md) | .NET 10 LTS, Blazor and EF Core versions | E1 | accepted |
| [0002](0002-data-access-and-database.md) | PostgreSQL 18 with EF Core and per-operation contexts | E1 | accepted |
| [0003](0003-telemetry-and-logging.md) | Structured JSON logs; no telemetry exporters | E1 | accepted |
| [0004](0004-test-strategy.md) | Test strategy and the `tests` service | E1 | accepted |
| [0005](0005-case-workflow.md) | Case status workflow as a domain-owned edge table | E2 | accepted |
| [0006](0006-audit-trail.md) | Audit information: audit fields plus an append-only trail | E2 | accepted |
| [0007](0007-roles-and-permissions.md) | Local Identity authentication, roles and the permission matrix | E3 | accepted |
| [0008](0008-veteran-lookup-mock-and-client.md) | Veteran Lookup: code-first mock, generated OpenAPI contract, resilient client | E4 | accepted |
| [0009](0009-attachment-storage.md) | Attachment storage and protection | E4 | accepted |
| [0010](0010-render-mode.md) | Render mode: Interactive Server, account pages static SSR | E5 | accepted |
| [0011](0011-ui-accessibility-and-mobile.md) | UI kit, accessibility approach, responsive shell, dashboard definitions and PWA policy | E5 | accepted |
| [0012](0012-test-results-module.md) | Test results, execution evidence and history inside the application | E5 | accepted |

The render-mode and accessibility records required by E1-S8 are 0010 and 0011.
