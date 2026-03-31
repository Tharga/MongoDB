# Feature: Secure communication between Monitor Client and Server

## Source
Security requirement for distributed monitoring

## Goal
Protect the SignalR communication channel between monitoring agents and the central server so that only authorized agents can connect and send data.

## Scope — to be discussed during implementation
This feature requires a design discussion before implementation. Options to evaluate:

- **Shared secret / API key**: simple pre-shared token sent as a header during SignalR negotiation
- **JWT bearer tokens**: agents authenticate and receive a token; server validates on connection
- **mTLS**: mutual TLS with client certificates — strongest but most complex to configure
- **Tharga.Communication built-in**: evaluate if Tharga.Communication has or plans auth support

### Considerations
- Should work for both localhost development (relaxed) and production (strict)
- Configuration should be simple — ideally one shared key in both client and server config
- Unauthorized connections should be rejected at negotiation, not after data is sent
- Evaluate whether CORS policy should be tightened (currently `SetIsOriginAllowed(_ => true)`)

## Acceptance Criteria
- [ ] Design decision documented in feature.md before implementation
- [ ] Unauthorized agents cannot connect to the monitor hub
- [ ] Authorized agents connect without manual certificate/token management in dev mode
- [ ] Configuration is simple (one setting per side minimum)
- [ ] Existing functionality works without breaking changes

## Done Condition
The monitor hub rejects unauthorized connections while authorized agents connect seamlessly.
