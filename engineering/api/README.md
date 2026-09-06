# Host API

The host is authoritative for application state. First-party Angular clients use REST and a ticketed JSON WebSocket for request/response operations and live updates. Both transports delegate application work to the same handler layer.

## Boundaries

REST controllers live under `host/src/MacroDeckHost/Api/Controllers/`. The first-party UI socket is `/ws/ui`; a one-time ticket is minted at `/api/ui-websocket/tickets`. Transport code should validate/translate transport concerns and delegate business logic to Application handlers.

Out-of-process plugins use a separate versioned HTTP/WebSocket JSON protocol. That protocol is a public compatibility surface and is documented in the [developer documentation](https://docs.macro-deck.app/reference/protocol/) and [ADR 0026](../decisions/0026-plugin-protocol-and-sdk-boundary.md).

Do not maintain a hand-written endpoint catalog here. Controllers, transport DTOs, protocol schemas, and generated/public reference documentation are the source of truth for exact methods and payload shapes.

## Server-to-client updates

Application state changes are mapped to UI event DTOs and sent through the UI transport. The WebSocket is for live state delivery and subscription-oriented interactions; it is not a second business-logic layer.

Realtime operations must remain responsive. Work that can block on an external provider belongs in a REST request or background component unless the dispatcher can answer from available state.

## Authorization

Authorization is deny-by-default. The trusted desktop path uses the dedicated loopback listener; remote/public clients authenticate normally and receive only the scope they need.

Security-sensitive details are documented in [authentication.md](authentication.md). Public plugin authentication belongs to the plugin protocol documentation rather than this internal UI transport note.
