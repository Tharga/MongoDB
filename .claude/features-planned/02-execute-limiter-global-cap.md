# Feature: ExecuteLimiter global cap aligned with connection pool size

## Source
Eplicta.Core request (2026-03-27, High priority)

## Goal
Prevent `MongoWaitQueueFullException` by ensuring total concurrent operations across all keys cannot exceed the MongoDB connection pool size.

## Scope
- Make `ExecuteLimiterOptions.MaxConcurrent` nullable (`int?`)
- Resolution order: (1) explicit user value, (2) `MongoClientSettings.MaxConnectionPoolSize` from parsed connection string, (3) driver default (100)
- Add a shared global `SemaphoreSlim` enforcing the resolved limit across all keys
- Keep per-key semaphores as secondary fairness mechanism

## Acceptance Criteria
- [ ] `MaxConcurrent` is `int?` — null means auto-detect from pool size
- [ ] Global semaphore prevents total concurrency from exceeding pool size
- [ ] Per-key semaphores still exist for fairness
- [ ] Tests cover: explicit value, auto-detected value, default fallback
- [ ] No `MongoWaitQueueFullException` under load when properly configured

## Done Condition
The limiter automatically stays in sync with the actual connection pool size without manual configuration.
