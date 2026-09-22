# ADR 0093: Plugins and integrations talk over a host-brokered message channel

Status: Accepted

## Context

Plugins and integrations had no way to reach each other without a direct dependency. The only
cross-participant path was `event.publish` into the trigger bus ([ADR 0010](0010-one-flow-engine-for-triggers-events-and-automations.md)),
which feeds the user's automation, not other plugins. A plugin cannot open a connection to another
plugin either: plugins are separate processes that only talk to the host
([ADR 0026](0026-plugin-protocol-and-sdk-boundary.md)).

Whatever carries these messages becomes public SDK and protocol surface, frozen once shipped, and must
keep every plugin built against an older SDK working.

## Decision

**The host is the broker.** `IMessageChannel` in `MacroDeck.Sdk.Messaging` is reached through
`IIntegrationContext.Messages`, and in a plugin also through dependency injection. Every participant, a
plugin or a built-in integration, addresses topics, never another participant. The host stamps each
delivered message with the sender's integration id.

**Three kinds.** An event reaches every subscription whose pattern matches, including the publisher's
own, at most once and in publish order per receiver. A command and a request reach the one participant
handling the topic; a request returns its handler's reply. Whoever handles a topic first keeps it until
it disposes the registration or goes away; a second claim is rejected and names the owner.

**Topics are a small grammar.** Two or more lowercase dot-separated segments, at most 128 characters.
Subscriptions take a topic or a `prefix.*` wildcard; handlers take exact topics.

**Payloads are opaque JSON**, at most 64 KiB. The SDK's typed helpers serialize with System.Text.Json;
the protocol never sees SDK types.

**On the wire it is a host api plus a capability kind**, following ADR 0026: the `messaging` host api
(`publish`, `send`, `request`, `subscriptions`) and the `messaging` capability kind (`event`, `command`,
`request`), declared at local id `provider`. No message type, error code or protocol major is added;
failures are existing codes refined by `messaging_` reasons.

**A plugin states its registrations as a whole table.** `subscriptions` replaces everything the plugin
listens to, tagged with the session that sent it. The SDK sends the table after every change and on
every connect, so a resume, a reconnect and a replaced session all converge without a delta protocol.
A replaced or pruned session's entries are removed; a detached one keeps them through the resume window.

**Registrations follow the integration's lifecycle.** Those made through `IIntegrationContext.Messages`
are released whenever the integration is shut down or initialized again. During that pass the SDK keeps
sending them, so the host holds the plugin's topics until the new initialization has registered again.

**The SDK declares the kind only on a host that offers it.** The SDK reads the host's descriptor before
opening a session. On an older host it declares nothing, so the plugin is not reported partially
incompatible for a capability it never asked for, and every channel call reports `Unsupported`.

**Commands and requests run off the session's dispatch loop.** A handler in the same plugin that calls
back into the host would otherwise queue its `host.invoke` behind the request it is answering.

**Messaging is budgeted apart from other host calls.** It has its own per-plugin rate, `subscriptions`
is bounded by size rather than rate, and a handler receives at most eight commands and requests at once,
so messaging cannot use up a plugin's invocation slots for button presses.

**`host:messaging` is declared, not enforced**, like every permission except `host:adb` (ADR 0092).

## Consequences

- Anyone can publish on any topic and claim any unclaimed topic. Senders are authenticated, not
  authorized; a handler checks `Sender` when it matters. Topic ownership depends on who registers first.
- Delivery is at most once. A subscriber that falls 256 events behind loses events, and nothing is
  replayed after a reconnect.
- A plugin built on this SDK declares one more capability on a host that offers the channel, even if it
  never uses it. Conformance checks that invoke the first declared capability skip it.
- `IIntegrationContext` grows by a default interface member, so existing implementations keep compiling
  and report the channel unsupported.
- Wildcards beyond a trailing `prefix.*`, retained messages and per-plugin grants are left open; each can
  be added without breaking the wire.

## References

- [Messaging between plugins](../../docs/src/content/docs/features/messaging.md)
- [`MessageBroker`](../../host/src/MacroDeckHost.Application/Messaging/MessageBroker.cs)
- [`RemoteMessageChannel`](../../sdk/src/MacroDeck.Plugin.Hosting/Integrations/HostApis/RemoteMessageChannel.cs)
