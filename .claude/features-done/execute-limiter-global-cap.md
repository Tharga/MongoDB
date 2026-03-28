# Feature: ExecuteLimiter global cap aligned with connection pool

## Originating Branch
develop

## Goal
Prevent `MongoWaitQueueFullException` by sizing the limiter per connection pool (per MongoClient), auto-detected from `MaxConnectionPoolSize`.

## Design
- Key semaphores by server key (same key `MongoDbClientProvider` uses to share `MongoClient` instances)
- One semaphore per connection pool, sized to `MaxConnectionPoolSize`
- `MaxConcurrent` becomes `int?` — null means auto-detect from pool size
- `MongoDbService` exposes `GetServerKey()` so callers can identify the pool
- `DiskRepositoryCollectionBase` passes the server key when calling the limiter

## Acceptance Criteria
- [ ] `MaxConcurrent` is `int?` — null means auto-detect
- [ ] Semaphore keyed by server key (one per connection pool)
- [ ] Pool size auto-detected from `MongoClient.Settings.MaxConnectionPoolSize`
- [ ] Explicit `MaxConcurrent` value overrides auto-detection
- [ ] Tests pass

## Done Condition
The limiter automatically protects each connection pool without manual configuration.
