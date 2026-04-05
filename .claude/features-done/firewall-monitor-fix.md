# Feature: Fix firewall opening and monitor database access

## Originating Branch
develop

## Goal
Firewall opens before monitor starts, monitor uses firewall-checked path, clear logging.

## Scope
1. Reorder UseMongoDB — firewall before monitor
2. MongoDbCollectionCache uses firewall-checked access
3. Clear firewall status logging

## Acceptance Criteria
- [ ] Monitor starts after firewall is open
- [ ] Monitor _monitor collection goes through firewall check
- [ ] Firewall status logged at Information/Error level
- [ ] Tests pass

## Done Condition
IP-locked firewall apps start successfully with monitor enabled.
