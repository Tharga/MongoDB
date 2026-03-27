# Feature: Show ExecuteLimiter queue count in CallView

## Source
Backlog (Medium priority)

## Goal
Display the number of calls waiting in the ExecuteLimiter queue for "Ongoing" operations in the CallView Blazor component, with live updates.

## Scope
- Expose queue depth from ExecuteLimiter
- Update CallView to display queue count for ongoing operations
- Implement live update mechanism (e.g. SignalR, timer-based refresh)

## Acceptance Criteria
- [ ] Queue count is visible in CallView for ongoing operations
- [ ] Display updates live as queue changes
- [ ] No performance regression from live updates

## Done Condition
Users can see how many operations are queued in the ExecuteLimiter directly in the Blazor CallView.
