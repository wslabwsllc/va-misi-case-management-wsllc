# 0005. Case status workflow as a domain-owned edge table

* Status: accepted
* Owner: E2

## Context

Spec 4.3 lists five allowed transitions (New→Triaged, Triaged→In Progress, In Progress→Resolved, Resolved→Closed,
Resolved→In Progress) and asks for "controlled" transitions. It does not name the initial status, say whether
unlisted transitions are allowed, or say which roles may perform which transitions (OQ-10, OQ-11, OQ-3). The UI must
offer only the transitions the current user may perform, and the server must enforce the same rule.

## Decision

- `CaseIntake.Domain.Cases.CaseWorkflow` holds the edges in an immutable
  `FrozenDictionary<CaseStatus, ImmutableArray<CaseStatus>>` behind `ICaseWorkflow` (`IsAllowed`, `NextStatuses`).
  It is the only definition of the workflow.
- New cases start as `New`. Every pair not in the table is rejected, including same-status, backward moves and
  anything from `Closed` (no reopen from Closed).
- `Case.TransitionTo(target, workflow, …)` guards in the domain; `CaseService.ChangeStatusAsync` checks first and
  returns results in this order: unknown enum → `Validation`; missing case → `NotFound`; edge not in the table →
  `InvalidTransition`; no `cases.transition.{From}.{To}` permission → `Forbidden`; stale `Version` → `Conflict`.
- `CaseDetailDto.AllowedNextStatuses` = workflow edges filtered by the actor's transition permissions, so the UI shows
  exactly what the server will accept.
- Resolved→InProgress is audited as `Reopened`; every other edge as `StatusChanged`.

## Alternatives considered

- A state-machine library (Stateless): more capability than five edges need, and another pinned dependency.
- Storing the workflow in the database: makes it configurable, but the spec fixes the edges, and a data-driven
  workflow would need its own validation and migration story.
- A `switch` inside the service: works, but the UI and tests would need a second copy of the rule.

## Tradeoffs

The workflow is compiled in; changing it is a code change plus tests (the exhaustive 25-pair test fails on any
drift). Permission names are derived from status names, so renaming an enum member would change permission strings.

## Consequences

The Government requirement change (E1-S9), if it touches the workflow, is a one-file change in Domain plus the
role matrix (E3) and the golden tests. UI code never hard-codes transitions.
