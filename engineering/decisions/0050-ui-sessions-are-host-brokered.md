# ADR 0050: UI sessions are host-brokered, and a configuration tree renders a transaction it does not own

Status: Accepted

## Context

[ADR 0038](0038-ui-model-and-declarative-dsl.md) gave Macro Deck a UI model that names no transport and a
DSL that produces trees and patches. Neither says who carries a tree to a client, and there was no
session concept anywhere.

Two kinds of code build a tree — an in-process integration holding a view, and a plugin on the far side
of a WebSocket — and both must be reachable through one API, or the renderer has to be written twice. A
plugin cannot be asked for anything synchronously
([ADR 0062](0062-ui-realtime-transport.md)). The failure the whole feature exists to prevent is a client
left holding a tree that will never change again.

Configuration is where a tree first meets real state, and it already has two established entry points
that are not alike: an integration's config flow is a stateful wizard whose manager encrypts secrets,
registers and releases the OAuth callback, replaces a single-configuration entry and re-initializes the
integration; an action's configuration is a flat parameter list persisted with the action instance. Two
facts decided how a tree relates to them, and neither is negotiable. **The host cannot see inside a
tree**, so a value a user types crosses it as bytes it does not interpret. And **secret classification is
derived from the declared field list** — a tree has no fields, so a configuration path terminating inside
one would reach persistence with nothing to classify and would store API keys in clear text.

## Decision

**The host owns the session. A provider produces trees, patches and faults; a client attaches, renders
and sends events; neither ever addresses the other.**

### The session is host state

The session id, the state machine (`Opening → Open → Draining → Closed`, plus `Invalidated` from
anywhere), the limits, the revision reached and the owning principal live in the session registry. The id
is a version-7 GUID, so it is unguessable and there is no enumeration oracle to protect. Because the host
owns the session, exactly one place decides it is over and exactly one place tells the clients.

### One provider abstraction, resolved blind

`IUiSessionProvider` is the only thing the broker talks to, and **no member returns tree or patch data** —
everything a provider produces travels back through a sink. That one-way rule is what lets a transport
method answer without ever awaiting a provider. The resolver returns the interface, so the broker never
learns whether it got an in-process adapter or a remote one, and a remote plugin deliberately does not
implement the in-process provider interface: it has no view, a synchronous build would block on a socket,
and since plugins register as integrations it would give the resolver two candidates for one id.

### The broker is a stateless relay

It holds no tree, applies no patch and re-serializes nothing. A payload is bounded by one forward-only
reader pass over the bytes the provider sent, and those same bytes are written to the client verbatim. A
deserialize-and-reserialize hop would drop unknown members, reorder map keys, renormalise numbers and
collapse duplicates, so the tree a client applies would no longer be the tree the provider produced.
Keeping that true on the outbound half needed a raw-JSON converter on the protocol serializer, because
the framework's element writer re-encodes and a payload leaving a .NET plugin crossed three such writes.

The consequence is deliberate: **revision continuity is the provider's obligation, and the broker's only
recovery is to ask for a snapshot.** When a patch does not apply — wrong base revision, no operations, no
revision advance, too large, too fast — the host requests a fresh tree and delivers that. It never
delivers an inapplicable patch and never stays silent.

### Both session modes exist from day one

`exclusive` and `shared` are both implemented, because the mode changes fan-out, snapshot targeting and
the event envelope, so it cannot be added additively later — a plugin shipped against an exclusive-only
protocol would have to grow one state machine per connected deck. **An unrecognised mode string is
enforced as exclusive**, because guessing "shared" would fan one client's tree out to every other client.

### Provider death ends the session, visibly

**Every terminal transition emits exactly one message to the attached clients before the record is
removed** — exactly one, so a client cannot see both a close and an invalidation for one death. One death
must also produce one *code*: a dropped plugin connection fails the in-flight invoke and ends the plugin
session at the same moment, so the broker classifies an invoke that failed because the link was already
gone as a disconnect rather than as a second, separate fault. Teardown is matched on the plugin *session*
id, so a reconnecting plugin's new sessions survive the old connection's teardown.

The provider's own fault text is logged, never relayed: it may carry an exception type, a stack trace or
a host path.

### A session belongs to the principal that opened it

Attach from any other principal is refused, including across the drain window, and **admin scope does not
bypass it**. A configuration surface is exactly where credential-shaped values are typed, so one device's
half-filled dialog must not reach another device's screen; an escape hatch would make the binding
advisory. Attach answers "not found" for any session that is not live, whatever the reason, because
knowing why does not change what the caller does next — but send-event distinguishes a recently closed
session from one that never existed, because a client that raced a close needs to tell "your view just
went away" from "you sent a bad id".

### Limits are compile-time constants advertised in the handshake

They are **not** configurable: a limit a user can move is a limit a plugin author cannot rely on. Two are
approximate and documented as such — node count across a patch stream is a conservative, self-correcting
bound, because a relay holding no tree cannot know a removed subtree's size, and the bound only ever
drifts upward, costing a snapshot.

Provider pushes ride the existing generic host-callback pair rather than minting message types, which
makes the limit verdict fall out for free: a refusal is that call's own result. The `ui` api is exempt
from the shared callback throttle because its rate is already bounded per session, and the per-session
limiter can ask for a resync where the shared bucket can only drop. A refused update asks for **one**
resync and refuses plainly after that; only a refusal landing *after* that resync was delivered ends the
session. Without that asymmetry there is no reachable terminal state and a provider can force a resync
forever.

### A configuration tree is a rendering; the transaction stays where it already is

A tree never completes a flow and never persists a parameter. The config flow's submit path remains the
only way it accepts values, and the ordinary action save path remains the only way an action instance is
written — so secret encryption, OAuth, single-configuration replacement and re-initialization are reached
identically whether a tree was rendered or not.

Three things follow, and they are what constrain future work.

**The declared field list is always served, even when a tree is on offer.** It is not merely a fallback
for a renderer that cannot handle the tree; it is the schema the host classifies secrets from. Suppressing
it once a tree exists would be a security regression, not an optimization.

**A plugin serving a tree classifies its own secrets.** Because the host cannot inspect the tree, a flow
collecting a sensitive value through one must say so when it completes. This is a real semantic difference
from the declared-field path, where the host infers it.

**No provider-to-host completion channel exists.** Adding a "commit these values" operation would make a
provider drive host state and would need a new protocol operation; it was rejected rather than deferred.

Entry points are told apart by surface *attributes* rather than by a new session field, which is what let
each later one — action configuration, folder-view configuration, widget configuration — be added without
touching the session contract. Widget configuration is one of them: its surface carries the widget id,
type and **stored configuration**, because a plugin cannot read the host's stored widgets. Its tree has
two named regions, `widget-properties` and an optional `widget-editor`; naming them is the point, since
inferring a layout from child order would make the split a convention a renderer with a different shape
of screen could not honour. What the regions are *called* belongs to Macro Deck; where they are *drawn*
belongs to the renderer, and nothing in the contract names Angular.

Both regions share one input-id namespace, because a region opens no scope: a top-level input's id is the
widget data key it configures. Scoping per region would write a key no widget schema has. The client
composes the draft **structurally**, walking the tree — object and array inputs are the only scopes — and
never splitting a node id on `.`, because a literal `.` is legal inside one key so a dotted path is
ambiguous by construction. That matters more than it looks: every widget schema leaves
`additionalProperties` unset, so a flat dotted bag validates, saves, and destroys the widget with nothing
red anywhere.

## Consequences

- The broker cannot repair a revision chain, only restart one, so every unrecoverable relay condition
  costs a full tree. The alternative is a stale client or a host-side copy of a model the host does not
  own.
- A tree cannot collect a value the transaction has no place for: anything it gathers has to reach the
  submit path through field names the surrounding transaction already knows.
- Secret handling is split by path — inferred by the host for declared fields, declared by the plugin for
  trees — so anything changing secret classification has to change both. Widget data carries no secret
  today, so that rule has nothing to attach to on that surface yet.
- Stored values seeding an action-configuration session cross to the plugin process, so secret-typed
  parameters cross **masked**. Expanding a configuration surface is UI interaction, not an intent to
  reveal a stored secret.
- A client that cannot render the offered UI model version never opens a session at all, so an old or
  restricted client costs the provider nothing. For configuration that is fine, because the declared field
  list is still there; **for widget configuration there is no second representation**, so a declined
  session must surface a visible rejection rather than an empty pane.
- The three session methods live on the existing UI transport rather than a second one, which would
  duplicate authentication, connection tracking and disconnect handling for no gain.

## Alternatives considered

- **A stateful broker holding the authoritative tree.** Costs a tree per open session, makes the host
  authoritative over a model the provider owns, and applying a patch means parsing and re-serializing it
  — which breaks the no-round-trip requirement outright.
- **A dedicated one-way push message type.** Adds protocol surface for a job the generic pair already
  does, and deletes the reply that carries the limit verdict.
- **Exclusive sessions only.** Cannot be widened additively.
- **User-configurable UI limits.** A limit that varies per installation is one a plugin author cannot
  design against.

## References

- [Issue #541](https://github.com/Macro-Deck-App/Macro-Deck/issues/541),
  [Issue #543](https://github.com/Macro-Deck-App/Macro-Deck/issues/543),
  [Issue #791](https://github.com/Macro-Deck-App/Macro-Deck/issues/791)
- [Macro Deck UI guide](https://docs.macro-deck.app/ui/)
