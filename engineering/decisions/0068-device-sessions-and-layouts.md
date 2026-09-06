# ADR 0068: Device sessions push full surface snapshots, and layouts are provider-registered descriptors

Status: Accepted

## Context

A plugin or integration could register hardware or a custom client as a device
([#584](https://github.com/Macro-Deck-App/Macro-Deck/issues/584)), and registration was the whole
contract: the host learned a device existed, its name, its capabilities and its presence. Nothing let
that device show anything or report a press back, so a registered Stream Deck or ESP32 panel sat in the
device list unable to act as a deck ([#585](https://github.com/Macro-Deck-App/Macro-Deck/issues/585)).

Two existing pipelines already did almost this. A browser client renders a folder over a realtime
WebSocket and reports presses against a client id; a plugin's UI provider
([ADR 0050](0050-ui-sessions-are-host-brokered.md)) renders a declarative tree the host brokers between
a provider and clients it never lets address each other. A device session needed the client pipeline's
concreteness — it renders an actual deck folder, not an arbitrary tree — built the UI session's way.

A device descriptor also carried an opaque `LayoutReference` string the host stored and handed back
without interpreting, with a standing forward reference to a layout abstraction that did not exist
([#384](https://github.com/Macro-Deck-App/Macro-Deck/issues/384)). The issue's own sketch had a provider
answer a synchronous `GetLayout(context)` call, which the host cannot bound.

## Decision

### The host owns per-device navigation state and pushes full snapshots

A device surface session holds the profile, the folder, the navigation history and the last surface
pushed — one instance per open session, independent of every other device and of every connected client.
A surface is the folder's complete render at a revision that only increases within that session; a
reconnect opens a fresh session with a fresh revision sequence and a fresh snapshot.

**A diff protocol was rejected.** The surface is one folder's worth of widgets, the push is already
debounced and content-compared so an edit that does not change what renders never ships, and a provider
that must reconcile a diff against a snapshot it may have dropped is a class of bug a snapshot cannot
have. The cost is a full push on every real change; the alternative is a provider-side merge that has to
be right on every device family before day one.

### Plugins never execute a widget's actions

The interaction router matches the reported widget id against the session's last pushed surface and hands
anything that resolves to the same handler a WebSocket client goes through. A provider that could run
flows itself would need the flow engine, the widget's private `Data`, and every other host service a flow
can call; sending only appearance data and keeping execution host-side is what lets the surface builder
serve a curated appearance and never touch widget data at all.

### A provider device is a navigation origin, not a transport client

`device:{id}` mints the same shape of id a client id has in the trigger and event pipeline, so a device's
press publishes folder changes and drives automations exactly as a client's does. It is an *origin*, not
a session identity: nothing about it implies a realtime connection, a signed-in user or a client
capability set, because a provider-registered device never signs in. That is also what makes
device-scoped navigation exact rather than incidental — navigating mutates only the one matching session,
so two devices on the same profile can sit in different folders.

**Short and long press are synthesized host-side at the browser clients' own threshold, per widget**, so
one hardware button behaves identically to one on-screen tap. State is keyed per widget id, not per
device, so a release on one widget cannot end another's in-flight press. A provider whose firmware
already distinguishes the two sends them directly and bypasses synthesis — the host does not re-derive
what the device already knows. Other interaction kinds (encoder, touch move, analog) are accepted and
reported unsupported: additive contract surface with no widget model behind it yet.

### Layouts are registered descriptors, referenced by devices

`ILayoutProvider` is a capability implemented independently of `IDeviceProvider`; a device points at a
layout through the existing opaque reference string and the two never share a lifecycle. That is what
keeps hardware support out of core: the device model gains no per-family geometry code, and a layout
provider can describe a surface no device provider ever registers a device for.

**Registration is push, mirroring device registration**, because a synchronous getter the host calls on
demand would have to bound an arbitrary provider call inline. A "get layouts" member exists only so the
host can recover its own view after a reconnect, and answering it is optional.

**A region's kind is an open string with a typed optional payload**, not an enum and not a sealed record
hierarchy. An enum would force every already-compiled `switch` to mishandle a member added later; a
sealed hierarchy has the same problem in a different shape — a plugin could not declare a pedal board
until some future release shipped a type for it, which is backwards for a contract meant to let
*providers* describe hardware Macro Deck has never heard of.

**Layout definition stays separate from profile content.** A descriptor describes hardware; the host
reads it to constrain and validate a profile and never writes back into it.

**The resolved layout is persisted with the device**, not just its reference string, so a stopped plugin
does not silently unconstrain a profile built against a fixed grid. Unregistering and a provider shutting
down behave the same way: the reference is retained and left unrefreshed, never dropped.

**Locking counts distinct resolved layouts among a profile's startup-profile devices, not devices.** A
profile is locked to a fixed grid only when those devices resolve to exactly one rows×columns pair.
Several devices sharing one layout still lock it — they agree by construction — while devices whose grids
disagree leave the profile editable and surface a warning naming them, rather than the host guessing
which is authoritative. A layout with zero or several grid regions constrains nothing, for the same
reason.

**Two visual capabilities describe relevance, not ability.** Most answer "can this surface draw X".
Widget spacing and corner radius answer whether the profile's value changes anything: rows and columns
are fixed by hardware, so the editor locks them, but spacing between physical keys is decided by the
chassis and the profile's value is simply inert — modelling that as a lock would assert something false.
Both default to false alongside the other flags, but the host reads an **absent** visuals block as
"honoured", so a provider that filled nothing in does not silently switch its users' controls off.

**There is no reflow between an incompatible layout and a device's physical geometry.** The host does not
clip, scale or validate the grid against a device's key count; a 3×2 device assigned a 5×3 profile is
handed the full grid. Reflow policy is inescapably device-specific — paging, scrolling and cropping all
make sense for different hardware — so one policy in the host would be right for no device, and it would
travel through the same compatibility guarantee as everything else in the SDK.

### The host-to-plugin asset channel is a new message-type set

An icon fetched through the devices API needs the host to hand a plugin bytes over more than one message,
the same chunked shape a plugin uploading to the host already has. Those existing types are fixed
plugin→host and are major-1 surface a client already ships against; flipping what they mean would
silently change an existing type rather than add one. `host.asset.*` is the same shape and bounds run the
other way, entirely additive.

## Consequences

- A provider that never overrides the session hook stays registration-only forever — the default is a
  no-op, not a stub that will eventually be called some other way.
- The host opens a session only once a plugin negotiates the device capability at version 2 or higher, so
  a version-1 plugin keeps working exactly as before.
- A rejected interaction — most often a press racing a surface push — and an unsupported one are both
  normal results, never a fault: the session stays open and the next valid interaction still works.
- The surface comparison is the definition of "render-relevant", so editing a widget's flows, which no
  surface field carries, is invisible to a device and costs nothing.
- The plugin protocol gains one capability kind and one host API for layouts, both additive within the
  existing major. The manifest permission vocabulary gains `host:layouts`, advisory like every other
  permission string — declared and exposed, enforced nowhere.
- A device referencing an id no layout provider has registered is not an error at any point: registration
  succeeds and its profile is simply unconstrained until a matching layout appears.
- `ILayoutProvider` deliberately has **no shutdown member**. The ordinary hardware case is one
  integration implementing both provider interfaces, so a single method body would satisfy both and be
  invoked twice, closing an already-closed transport the second time. A provider releases what it
  acquired in `IIntegration.ShutdownAsync`, which the host already calls exactly once, and the host
  withdraws the registered layouts itself.

## References

- [Issue #384](https://github.com/Macro-Deck-App/Macro-Deck/issues/384),
  [Issue #584](https://github.com/Macro-Deck-App/Macro-Deck/issues/584),
  [Issue #585](https://github.com/Macro-Deck-App/Macro-Deck/issues/585)
- [Device providers](../../docs/src/content/docs/sdk/devices.md),
  [Layout providers](../../docs/src/content/docs/sdk/layouts.md)
