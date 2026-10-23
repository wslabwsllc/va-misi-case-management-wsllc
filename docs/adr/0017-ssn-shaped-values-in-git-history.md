# 0017. Git history is not rewritten to remove SSN-shaped test values

* Status: accepted (decided 2026-10-21; recorded 2026-10-22)
* Owner: E1

## Context

Review finding F05 (rows R24/R35): synthetic data must not look like a real person's SSN. Early commits added
SSN-shaped test values in **issuable** area numbers. They were typed as placeholders and are not taken from any
person's data, but numbers in those areas can be issued, so they are potentially valid SSNs. The full values are
deliberately not repeated in this record.

A scan of every commit on every branch with the guard's own pattern (2026-10-22) found these additions:

| Commit | Date | Values added |
|---|---|---|
| `d4ab5ac` | 2026-10-16 | area 123 (6), area 555 (1), area 111 (2) |
| `790c43e` | 2026-10-19 | area 123 (3) |
| `55c3397` | 2026-10-20 | area 123 (1): the example in the Government's Veteran Lookup contract |

`6acbbb1` (2026-10-19) replaced the test values with never-issued ones. The current tree is
clean: `SyntheticSsnGuardTests` fails the build for any SSN-shaped literal outside the never-issued areas (000, 666,
900-999). Its one exemption is the Government's example in `docs/contracts/veteran-lookup.spec.openapi.yaml`, which is
kept byte for byte as delivered and is pinned by SHA-256. The remaining question was whether to remove the old values
from history as well.

## Decision

History is **not** rewritten. The old values stay in the commits listed above, and they are recorded instead of
removed: here, and under "Known gaps" in [assumptions-and-limitations.md](../assumptions-and-limitations.md). The
commits that added or removed them are listed by:

```
git log --all -G '123-45-67|"1234567|555-44-33|1112233' --oneline
```

(Two matches are not SSNs: `1cdafc0` matches other digits, and `3130470` matches only because the known-gap entry
quoted an earlier version of this pattern.) New SSN-shaped
values are kept out by the guard test, not by policing history.

## Alternatives considered

- **Rewrite history with `git filter-repo --replace-text` and force-push.** Removes the values from every reachable
  commit. Rejected: every clone, worktree and open pull request would be invalidated, all contributors would have to
  re-clone during the run-up to the demonstration, and every commit ID cited in the docs (contributor declaration,
  change implementation record, ADRs) would change. Copies already pushed to forks or caches would not be removed.
- **Squash the repository into a fresh initial commit.** Rejected for the same reasons, and it would also discard the
  Git history that the contributor declaration (RFQ E.3) is derived from.

## Tradeoffs

Gained: stable commit IDs, intact contributor history, and no disruption to open work. Given up: the potentially valid
values remain readable by anyone with access to the repository's history. The risk is low because the values are
synthetic and not linked to any name or record, and the tree that is reviewed and demonstrated contains none apart from
the Government's own example.

## Consequences

- The known-gap entry stays until history is rewritten or the risk is accepted at review.
- `SyntheticSsnGuardTests` must keep running in every build; it is what stops new values from reaching history.
- If a clean history is required later (for example before the demonstration baseline), the team agrees a freeze,
  runs `git filter-repo --replace-text` with the recorded values, force-pushes, has everyone re-clone, and updates the
  commit IDs cited in the docs. This record is then superseded.
