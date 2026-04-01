# Feature: Monitor Communication Security

## Originating Branch
develop

## Goal
Expose API key configuration through Monitor.Client and Monitor.Server registration methods so the SignalR channel can be secured.

## Scope
1. Add `apiKey` parameter to `AddMongoDbMonitorClient`
2. Add `primaryApiKey`/`secondaryApiKey` parameters to `AddMongoDbMonitorServer`
3. Update README with security configuration examples
4. Verify end-to-end

## Acceptance Criteria
- [ ] Client can send API key via registration method
- [ ] Server can validate API keys via registration method
- [ ] No keys = accept all (backwards compatible)
- [ ] README documents configuration
- [ ] Tests pass

## Done Condition
Monitor hub can be secured with API keys. No breaking changes.
