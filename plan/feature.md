# Feature: executelimiter-queue-log-debug

## Goal

Resolve GitHub issue [#118](https://github.com/Tharga/MongoDB/issues/118) (filed by Eplicta, 2026-06-10): demote the `ExecuteLimiter` "Queued {queueCount} executions" log from **Information** to **Debug**.

## Background

`ExecuteLimiter.ExecuteAsync` logs `"Queued {queueCount} executions for {serverKey}."` at Information whenever `queuedCount > 1`. Under normal concurrent load this fires on nearly every operation — ~4.7M Information traces/week from a single Eplicta app in Application Insights, their largest single log source, with no actionable signal (`queueCount` typically 2–5, routine concurrency-gate bookkeeping).

The genuinely useful diagnostics in the same method are already at `Warning` (saturation, misconfiguration capping) and stay as-is.

## Scope

In scope:
- Change the single `_logger?.LogInformation(...)` call at the `queuedCount > 1` branch to `LogDebug`.
- Add a regression test pinning the message to Debug (and asserting it is never Information).

Out of scope:
- The optional configurable-threshold enhancement (`QueueLogThreshold`) suggested in the issue — the consumer confirmed a straight demote is sufficient; lowest-risk fix.
- NuGet dependency refresh — keep the release focused.

## Acceptance criteria

- The "Queued ... executions" message logs at `Debug`.
- The `Warning`-level saturation/misconfiguration lines are unchanged.
- A test verifies the message is emitted at Debug and never at Information.
- Full test suite (non-Database) passes.

## Done condition

PR open from `feature/executelimiter-queue-log-debug` → `master` referencing "Closes #118", after the close-out commit removes `plan/`. Ships in the next release (2.10.16).
