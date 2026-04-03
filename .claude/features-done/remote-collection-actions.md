# Feature: Full Remote Action Support

## Originating Branch
develop

## Goal
All dashboard actions work for remote collections/calls. Fix local DB access bug for remote collections.

## Scope
1. Fix GetInstanceAsync/RefreshStatsAsync to not access local DB for remote collections
2. Add remote Find Duplicates (GetIndexBlockersAsync)
3. Add remote Explain for calls
4. Add remote Reset Collection Cache (all agents)
5. Add remote Clear Call History (all agents)

## Acceptance Criteria
- [ ] Collection detail dialog opens without local DB errors
- [ ] Find Duplicates works remotely
- [ ] Explain works on remote calls
- [ ] Reset/Clear delegates to all agents
- [ ] Tests pass

## Done Condition
All dashboard actions work identically for local and remote data.
