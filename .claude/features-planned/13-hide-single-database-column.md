# Feature: Hide database column and filter when only one database

## Source
Backlog (New)

## Goal
When there is only one database, the Database column and filter dropdown add noise. Hide them automatically, matching the existing pattern used for Source and Configuration columns.

## Scope
- CallView.razor: add `_showDatabase` flag, hide Database column and dropdown when only one database
- CollectionView.razor: add `_showDatabase` flag, hide Database column and dropdown when only one database

## Acceptance Criteria
- [ ] Database column is hidden when all data comes from a single database
- [ ] Database filter dropdown is hidden when all data comes from a single database
- [ ] Both appear when multiple databases are present
- [ ] Applies to both CallView and CollectionView

## Done Condition
Database column and filter are only visible when there are multiple databases, matching the behavior of Source and Configuration columns.
