# MacroDeck.Plugin.Protocol

The wire contracts for the [Macro Deck](https://github.com/Macro-Deck-App/Macro-Deck) plugin
protocol: a versioned HTTP handshake and a JSON WebSocket session that any language can implement.
This package is pure contract - types, constants and pure rule functions, no transport.

What is in here:

- `Envelope` - `ProtocolEnvelope`, the tolerant reader and writer, message types and directions.
- `Handshake` - registration and session requests and responses, declared capabilities.
- `Capabilities` - the `capability.*` message payloads.
- `Limits`, `Reconnection`, `Correlation` - timeouts, queue limits, backpressure, backoff, resume and
  correlation rules, all as pure functions so both peers agree by construction.
- `Errors`, `Versioning`, `Serialization` - error and close codes, version negotiation, the one fixed
  `JsonSerializerOptions`.

If you are writing a plugin in .NET, use [`MacroDeck.Plugin.Hosting`](https://www.nuget.org/packages/MacroDeck.Plugin.Hosting)
instead - it speaks this protocol for you. Reference this package directly only when implementing the
protocol yourself.

The specification lives at [docs.macro-deck.app/reference/protocol](https://docs.macro-deck.app/reference/protocol/).

Licensed under Apache-2.0.
