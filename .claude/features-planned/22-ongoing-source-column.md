# Feature: Source column on Ongoing calls tab

## Source
UI gap — Ongoing tab doesn't show Source column

## Goal
The Ongoing tab in CallView should show the Source column and filter when multiple sources are present, same as Last and Slow tabs.

## Scope
- Verify that _showSource is evaluated when switching to Ongoing tab
- Ensure OnCallChanged triggers Source column visibility update
- May need to refresh _sources list when ongoing calls arrive from remote agents

## Acceptance Criteria
- [ ] Source column visible on Ongoing tab when multiple sources exist
- [ ] Source filter works on Ongoing tab

## Done Condition
Ongoing tab shows source information consistently with Last and Slow tabs.
