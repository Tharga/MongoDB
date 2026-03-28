# Plan: Fix duplicate detection via specific index definition

## Steps
- [x] 1. Write tests for `GetIndexBlockers` — with name, without name, and no duplicates
- [x] 2. Fix `GetIndexBlockers` to fall back to key field matching when no name match
- [x] 3. All 208 tests pass
- [x] 4. Commit
