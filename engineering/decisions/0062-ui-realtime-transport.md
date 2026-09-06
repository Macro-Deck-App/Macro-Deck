# ADR 0062: UI realtime is a ticketed JSON WebSocket, and it never blocks on a provider

Status: Accepted

## Context

The first-party UI needs bidirectional live state on both host listeners. SignalR supplied that, at the
cost of a second protocol beside the plugin one and a reusable bearer credential placed in the WebSocket
URL — a credential that lands in proxy logs and browser history.

Two behaviours of the old transport also had to be fixed rather than carried over. A slow
integration call inside a hub invocation blocked later calls from the same connection, so a provider
outage made unrelated UI operations appear frozen
([#189](https://github.com/Macro-Deck-App/Macro-Deck/issues/189)). And every variable change published
one message to every connected client whether or not anything on screen referenced it, with no
coalescing — while notification dispatch is awaited on the publisher's thread, so the integration
polling loop blocked on each send ([#497](https://github.com/Macro-Deck-App/Macro-Deck/issues/497)).
An integration may create variables from user-selected external entities, so the count is bounded by the
user's installation rather than by the integration.

## Decision

### A small versioned JSON WebSocket at `/ws/ui`

Negotiated as `macrodeck.ui.v1`. Clients mint a 256-bit, 15-second, single-use ticket through
authenticated HTTP and present only that ticket during the upgrade; the host stores only ticket hashes
and binds each ticket to the trusted-loopback or public listener class. No reusable bearer credential is
ever accepted from a WebSocket URL.

The endpoint does not filter the browser `Origin`, because public listeners must stay usable through
local IP addresses and reverse proxies. Cross-site ticket theft is prevented at the authenticated
ticket-minting route instead, which is deliberately excluded from the host's permissive CORS branch.

The endpoint has a closed allowlist of UI operations and keeps the plugin WebSocket's protocol,
authentication and permissions entirely separate; only protocol-neutral framing and bounded-send
mechanics are shared.

### The dispatcher never waits indefinitely on a provider

The sequential UI dispatcher must stay responsive: ping and cancellation frames bypass domain work, and
provider calls are either bounded or moved off this transport.

- Prefer host-side cached state populated by background polling. A cache miss means "not known yet"; do
  not invent a disconnected or empty value when the host cannot distinguish that from a provider failure.
- An unavoidable live provider call gets a hard cancellation bound and returns a safe "not available"
  result rather than holding the connection.
- Slow user-initiated reads that are neither cacheable nor safely bounded belong on REST, where one long
  request does not occupy the invocation path. Better still is a read a dialog performs for itself: the
  music player's catalog and device list are read inside the picker's own UI session, so neither reaches
  a shared transport at all.
- Polling clients keep at most one read in flight, so a slow provider cannot build an unbounded queue.
- Invocation processing stays ordered rather than parallel; allowing concurrency would only hide blocking
  calls.

### Variable pushes are coalesced batches gated by client interest

Changes are enqueued, coalesced by variable id over a short window, and pushed as one event carrying
upserts and deleted ids. Coalescing is queue-driven, so an idle host produces no variable traffic and no
empty messages, and a batch is chunked so no message carries more than 200 items.

Each id is resolved against the registry at publish time, not at enqueue time, so a variable deleted
within the window is reported as a deletion rather than delivered as an upsert; upserts and deletions
are therefore disjoint within a message.

A connection receives every batch by default and may call `WatchVariables` to declare the set of names
it needs, which **replaces** any previous declaration. That call answers with the current values of the
declared names, so the caller renders immediately rather than waiting for the next change. Interest is
per connection and does not survive a reconnect.

Client interest governs the UI transport only. Widget state bindings, the variable-changed trigger,
label rendering and condition evaluation run identically whether or not any client is connected or
watching, and the full snapshot stays available unpaged over REST.

## Consequences

- UI clients reconnect through the native WebSocket implementation every two seconds for as long as a
  connection is desired; there is no exponential backoff.
- Provider timeouts degrade one read rather than the entire client connection, and new operations should
  answer from host state whenever possible.
- A client that declares a narrow variable set and then reads a name outside it sees a value that never
  updates, and a variable rename is not followed. Declaring is a full-set replacement, so a client tracks
  its own live set and re-declares on change and on reconnect.
- Variable pushes arrive up to roughly two coalescing windows after the change, and only the final value
  in a window is observable to clients. Paths that need immediacy have their own transports.
- Per-recipient sends are individually isolated, so one slow client cannot halt the pump.
- Plugin WebSocket compatibility remains governed by
  [ADR 0026](0026-plugin-protocol-and-sdk-boundary.md); this transport is not public surface.

## References

- [Issue #189](https://github.com/Macro-Deck-App/Macro-Deck/issues/189),
  [Issue #497](https://github.com/Macro-Deck-App/Macro-Deck/issues/497)
