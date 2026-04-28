# Planned features

Index of open feature specs in this folder, with dependencies, effort estimate, and a suggested implementation order. Reorder if consumer priority dictates; the ordering below optimises for shipping low-risk wins first and letting the mandatory dependency chains (lock stack) sit together.

| Order | # | Feature | Depends on | Effort | Notes |
|---|---|---|---|---|---|
| 1 | [27](27-mcp-action-parity.md) | MCP action parity with Blazor components | — | Small | 6 tools following the two already-shipped ones (touch, rebuild_index). Mostly boilerplate, tests around `IRemoteActionDispatcher` add some depth. Independent of everything else — good parallel candidate. |
| 2 | [33](33-keyset-pagination.md) | Keyset-paginated `GetPageAsync` / `GetPageProjectionAsync` | — | Large | New `PagePosition` / `CursorPage<T>` / `CursorToken` types, cursor token encoding, non-unique-sort tiebreaker correctness, many edge cases (empty result, boundary conditions, cursor-from-other-sort rejection, index-absent fallback). Lots of tests. |
| 3 | [29](29-transactions.md) | Multi-document transactions | — | Large | MongoDB transaction semantics (60s timeout, replica-set requirement), session handling, integration with index management and monitoring. Foundational for #30. |
| 4 | [30](30-generalized-document-lock.md) | Generalised document lock with commit-time update/delete decision | #29 | Large | Lease-based API, per-document commit decisions, multi-collection locking, lock expiry mid-lease. Builds on the transaction primitive from #29. |
| 5 | [31](31-refactor-lock-for-update-delete.md) | Refactor `LockForUpdate` / `LockForDelete` as wrappers over #30 | #30 | Small | Mechanical consolidation once #30 is in place. Behaviour-preserving. |

## Done

See [features-done/](../features-done/) for completed specs. Most recent:
- **#32** — `GetAsync` / `GetProjectionAsync` on driver cursor (April 2026)
- **#28** — `ExecuteManyAsync` for streaming custom queries (April 2026)

## Notes on ordering

- **#27 first** because it's independent and unblocks MCP agent functionality; small and low-risk.
- **#33 before the lock stack** because it's independent work with its own design concerns; bundling it with #29/#30/#31 would make the lock stack harder to review.
- **#29 → #30 → #31** is a mandatory chain. Ship them together in sequence so the generalised API lands with its wrappers in place rather than leaving `LockForUpdate`/`LockForDelete` temporarily duplicated.
- **Breakpoint between #33 and #29** — if consumer pressure says the lock stack must go first, swap the blocks. The prerequisites inside each block stay intact either way.
