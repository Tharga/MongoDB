# Plan: ExecuteLimiter global cap aligned with connection pool

## Steps
- [x] 1. Expose server key: made `GetServerKey` on `MongoDbClientProvider` internal static, added `GetServerKey()` to `IMongoDbService`/`MongoDbService`
- [x] 2. Made `MaxConcurrent` nullable in `ExecuteLimiterOptions` — null means auto-detect
- [x] 3. Changed `ExecuteLimiter` to key by server key, auto-size semaphore from `maxConnectionPoolSize` (or explicit override)
- [x] 4. Updated `DiskRepositoryCollectionBase` to pass server key and pool size
- [x] 5. Updated test — default is now null instead of 20
- [x] 6. All 205 tests pass
- [~] 7. Commit
