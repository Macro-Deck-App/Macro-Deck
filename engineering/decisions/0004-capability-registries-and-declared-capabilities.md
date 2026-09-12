# ADR 0004: Capabilities are optional interfaces, discovered by registry

Status: Accepted

## Context

Integrations vary widely in what they contribute: music players, weather stations, configuration
flows, variables, icons, profiles. A fixed base class carrying every possible responsibility would
force empty implementations and couple unrelated features. The host needs to ask "which enabled
integrations can do X" without knowing anything about a particular integration.

Three further problems fell out of that model as it grew.

**Provider-local ids are only unique within one integration**, and the id rules for actions, events,
provider instances and variables had drifted apart — with the rules living in host-only code, so
plugin authors could not follow them.

**"What an integration can provide" and "what is registered right now" were the same question.** An
unconfigured integration reported its real action count next to `Variables: 0`, because variables are
registered only once an integration is enabled *and* initialized. A user had to configure an
integration to find out what configuring it would give them. Making the counts honest must not
initialize anything: several integrations open sockets, load native libraries or call an API during
`InitializeAsync`, and browsing a page must never trigger that.

**A remote plugin has no compiled .NET type to cast.** Something has to turn a negotiated set of
capability kinds into an object the rest of the host can cast exactly as it casts a built-in
integration.

## Decision

### The interface is the query

Every integration implements `IIntegration` and additionally implements any capability interfaces it
supports (`IMusicPlayerProvider`, `IWeatherProvider`, `IConfigFlowProvider`, `IVariableProvider`,
`IIntegrationIconProvider`, `IProfileProvider`, …). A host-side registry per capability enumerates
`IIntegrationRegistry.Integrations`, keeps the enabled instances implementing that interface, and
prefixes each provider-local id with the integration id.

Adding a capability is additive: a new SDK interface plus a new registry, with no change to
`IIntegration` or to existing integrations. Because the enable check runs on each enumeration rather
than being cached, disabling an integration removes its instances from every registry at once.

### One shared identity model

Authors declare local ids; `MacroDeck.Sdk.Identity` validates the owner and the local id and derives
the qualified form `ownerId::localId`. A caller must not submit an already-qualified id where a local
id is expected. Owner ids use the package-style reverse-domain grammar, with a small separate
category for host-internal providers whose persisted ids cannot be renamed. Declared ids are stable
lowercase kebab-case; runtime resource ids use a broader bounded grammar, because provider instances
can be GUIDs or externally derived. `::` is therefore reserved and a provider-local id must not
contain it.

Declared identities and duplicates are validated before an integration exposes capabilities, so a
malformed or duplicate id fails at registration instead of becoming silently unreachable.

### Declared is not registered

`IIntegration.Actions` and `IVariableProvider.DeclaredVariables` are the **catalog**. They are
readable on any registered instance — enabled or not, initialized or not — and reading them must be
side-effect free: no connecting, no process spawning, no filesystem or network access, no host state
created. `ProvidedVariables` remains the **runtime** list and is the only thing the polling service
registers.

Counts come from the catalog; values come from the runtime.
`GET /api/integrations/{id}/capabilities` serves the browsable catalog and assigns every item one
status from a single vocabulary — `Ready`, `SetupRequired`, `AvailableAfterSetup`,
`IntegrationDisabled`, `Unavailable` — resolved in that precedence. `SetupRequired` deliberately
outranks `IntegrationDisabled`, because an unconfigured `IConfigFlowProvider` is also a disabled one
and "Integration disabled" is the less actionable of the two true answers.

**This read path deliberately does not filter by enabled state** — not filtering is the point of a
catalog. The deviation is scoped to reading: the handler never resolves, executes or registers
anything. `GetActions` keeps its enabled filter, because it feeds the action picker and widening it
would let a user bind a button to an action that silently does nothing.

### A remote plugin becomes one of a closed family of adapters

A connected plugin's capabilities become one of a fixed set of concrete `RemotePluginIntegration`
subclasses, chosen once at registration by a factory switch over which optional interfaces the
negotiation accepted — never a dynamic proxy deciding at call time what it implements.

The base class unconditionally implements every interface whose mere presence carries no information
(a provider with an empty catalog is indistinguishable from one that is absent). Three are different:
`IIntegrationIconProvider`, `IConfigFlowProvider` and `IDynamicEventOptionsProvider`, whose presence
the host reads *before* calling a member — `HasIcon` on a summary, and the rule that an integration
exposing a config flow starts disabled until configured. The factory switches on those three booleans
and returns one of eight leaf classes, so `is IConfigFlowProvider` is a normal type check decided from
what the plugin actually declared.

`DispatchProxy` and `Reflection.Emit` were rejected rather than merely disliked: the host ships
trimmed and ReadyToRun, and both depend on runtime code generation that trimming cannot statically
analyze. Eight hand-written classes are also eight things a reviewer can read against the interfaces
they claim.

**No registry ever learns that a plugin exists.** The factory is the only place in the host that knows
`RemotePluginIntegration` is a concept; every capability consumer keeps filtering by interface.

A remote plugin's catalog is a `describe`-refreshed snapshot rather than a value read off a compiled
type, so every synchronous SDK member reads that cached snapshot instead of blocking on a round trip.
The window between a plugin-side change and the snapshot catching up is recorded per capability kind
in the published capability parity matrix.

A consumer that genuinely needs "did this plugin declare kind X" reads
`IDeclaredCapabilityKinds.DeclaredCapabilityKinds` rather than trusting a cast.

## Consequences

- Declared counts are stable across not configured, disabled, enabled and temporarily disconnected.
  An integration can legitimately hold two variable lists: Voicemeeter declares 51 always but provides
  none while Voicemeeter is not running.
- Config-dependent providers report a template built from `VariableNameTemplate.Placeholder`, whose
  delimiters cannot occur in a canonical variable name and so can never be registered as a real one.
- The SDK contract is now explicit that `Actions` and `DeclaredVariables` must be enumerable before
  initialization and free of side effects. A unit test asserts that reading a catalog leaves
  `IsInitialized` false and the variable registry empty; that assertion is the guard, not this prose.
- Plugin authors and host integrations follow the same published id rules, which makes capability
  identity a public SDK compatibility contract.
- A new optional interface that changes what an integration *is*, rather than what it can be asked to
  do, doubles the leaf count. Sixteen is the ceiling: the next such interface gets its own registry and
  standalone adapter, the way `ui` and the action icon provider already do.
- Trimming and Native AOT stay viable without a reflection-preservation allowlist.

## Alternatives considered

- **A fat base class exposing every capability.** Forces empty implementations.
- **A separate `IDeclaredVariableProvider` interface.** This is a second view of an existing
  capability, not a new one; a default interface member changes no existing provider.
- **Registering a disabled integration's variables anyway.** Puts unusable variables into every
  picker, and registration is exactly the side effect being designed out.
- **Initializing an integration on demand when its catalog is requested.** Opening sockets because a
  user opened a page is the behaviour being designed out.
- **A registry-visible "is this remote" flag.** Precisely the leak this pattern exists to prevent.

## References

- [Issue #268](https://github.com/Macro-Deck-App/Macro-Deck/issues/268),
  [Issue #408](https://github.com/Macro-Deck-App/Macro-Deck/issues/408),
  [Issue #413](https://github.com/Macro-Deck-App/Macro-Deck/issues/413)
- [Capabilities reference](https://docs.macro-deck.app/features/)
