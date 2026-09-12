# ADR 0081: Variables come from one provider catalog and carry attributes and a write capability

Status: Accepted

## Context

A variable was a scalar and little else: a name, a type, a string value, a classification and decimal
places. Four gaps followed, each worked around somewhere else.

**Two provider contracts for one concept.** `IVariableProvider` declared a small fixed set materialized at
startup; `IDynamicVariableProvider` ([#760](https://github.com/Macro-Deck-App/Macro-Deck/issues/760))
exposed a browsable catalog for identities that only exist at runtime — Home Assistant entities, ADB
devices, OBS sources, MQTT topics — where a bridge alone can carry thousands. That is a difference in
cardinality and materialization policy, expressed as two unrelated SDK contracts, two registration paths
and two entries in the UI, so one integration appeared twice on the Variables page. The registry
underneath never had this split: both paths already shared one reuse function and one definition index,
differing only in the shape of local id they permit.

**There is nowhere to put a unit.** The History Graph widget declared its own unit string, configured per
widget, for a number that comes from a variable, so any second consumer has to be told again. The same
hole appears from the other side in [#366](https://github.com/Macro-Deck-App/Macro-Deck/issues/366): a
playback position in seconds cannot say it is a duration, so the proposal there attaches display metadata
to the action that happens to write the value rather than to the value.

**Only user variables can be written.** A provider variable was structurally read-only to everything
except its own provider, so a widget that wants to change one could not.

**Writing was modelled as an action, separately from reading.** The slider action contract was a second,
parallel way to describe "this number can be read and set", and every action implementing it already had
a read-side variable for the same underlying value. The read side and the write side of one value were
two unrelated declarations, and only the slider could join them.

Two mechanical constraints bound the answer. Templates are Scriban and the renderer puts **typed
primitives** into the `vars` object, which is what makes filters, comparisons and truthiness work;
replacing them with a container naively breaks all three, and worst of all `{% if vars.x %}` would
silently evaluate to true for every variable, because object truthiness is "not null". And variable names
are `[a-z0-9_]`, so a dot is not a legal name character — an attribute path is a different thing from a
name wherever a name is stored.

## Decision

**A variable is a value, a set of attributes describing it, and a declaration of what can be done to it.
The value stays scalar wherever a variable is referenced; attributes hang off that reference; writing is
a capability of the owning provider, not a separate action.**

### The value stays scalar; attributes are a suffix

`vars.x` continues to be the value with its current type semantics, by unwrapping the container in the
renderer's own template context. `vars.x.unit` and the other attributes are reachable on the same
reference, and no existing template changes.

**Attribute paths exist only inside template expressions.** Everywhere a variable is *stored* as a
reference — a widget's value binding, a structured `$var` reference in an action parameter — a plain name
stays a plain name, because those references mean the value. This keeps every persisted format unchanged
and confines the path concept to free-form template text.

### Attributes are a typed core plus an open remainder, split by volatility

The host acts on a fixed, validated set — unit, semantic kind, decimal places, display name, and
min/max/step — because these drive real behaviour and cannot be untyped strings the host has to guess
at. Anything else a provider wants to expose goes into an open map the host does not interpret; it is an
extension point, not a way to introduce host behaviour without a contract change.

The typed set splits by how often it can change. **Static attributes** are declared once and cached with
the entity. **Volatile attributes** — min, max, step — may change with every read and travel with the
value: a media player's seek range is its current track's length, and an OBS input gain-boosted in
Advanced Audio Properties reports a maximum above 100. Pinning these to the declaration would make either
case unrepresentable, which is why a read returns a value *and* its volatile attributes rather than a
bare scalar.

`SemanticKind` answers "what does this value mean", as opposed to the type, which answers "how is it
stored and compared" — so a duration in seconds renders as `03:07` and a byte count as `1.5 KB` without
any widget knowing. It is an **open string with well-known values**, not a closed enum: the host formats
what it knows and falls back to plain numeric rendering, so a plugin can introduce a kind without a
protocol change and the failure mode of an unknown one is "not specially formatted" rather than an error.

### One provider contract with a materialization policy

The two contracts collapse into one catalog of definitions, addressable by definition id, browsable as a
tree and pageable. "Static" and "dynamic" stop being two kinds of variable and become one property of a
definition:

- **Eager** — materialized as a registry entry as soon as the provider is initialized. This stays bounded
  by contract, because every eager variable is in every consumer's candidate list forever.
- **On demand** — the definition exists in the catalog but becomes a registry entry only when something
  references it, which is what keeps a provider with thousands of entities out of every consumer's
  per-evaluation candidate list.

**Binding is the only bridge between the two worlds.** Once bound, a dynamic variable is an ordinary
registry entry: template rendering, condition evaluation, widget bindings, the invalidation index and the
client-side variable store keep working against plain names, with no dynamic-provider awareness anywhere
along that path.

**The binding, not the variable, is what gets persisted**, in its own store. A binding carries the owning
integration, the provider-local resource id, the name, the type and when it was created — enough to
re-resolve and re-subscribe the same resource on every future start without the user re-binding it. A
dynamic variable is materialized fresh from its binding on load and never separately serialized.

Only bound ids are ever subscribed. Discovery is purely for browsing: never cached, never persisted, and
never on its own causing a registry entry or a subscription to exist.

The declared/registered/available split from
[ADR 0004](0004-capability-registries-and-declared-capabilities.md) gains a fourth state between
registered and available: **bound but unresolved** — the user configured it, it has a name and a place in
the namespace, but the provider cannot currently read it. That is distinct from "not configured", since
the binding *is* the configuration, and from "available", which additionally requires a successful read.

### Writing is a capability of the owner

A variable declares whether it can be set, and the owner implements a single set method. The declaration
is separate from the implementation so the UI can decide *before* a write whether to offer a control at
all.

**A capability takes no parameters beyond the value.** This is the substantive difference from pointing a
variable at an action: an action needs its target bound at configuration time — which connection, which
input — whereas the variable **is** the bound target already. It is also why capabilities do not appear in
the template namespace: they are operations, not data.

`IVariableService` gains a single `SetValue` that dispatches on the entity's owner, so a write to a
variable whose owner declares no write capability is refused in one place rather than at three call
sites. A widget-classified variable is written only by its reconciler, so a UI write would be overwritten
on the next reconcile: that leg answers "not writable" rather than dispatching. The existing
classification check survives as the rename-and-precision gate — renaming a provider variable is still
not a user's to do — and the provider's own report path keeps its caller-must-be-owner check, because
authorship of a *report* is a different question from permission to *write*.

### The slider binds to a variable

The slider widget targets a writable numeric variable and nothing else: its range comes from the
variable's volatile attributes, its formatting from the semantic kind and unit, and a drag writes through
the capability. The slider action contract and its per-widget action binding are removed. The widget keeps
per-field range fallbacks, used only where the variable declares no volatile attribute of its own, because
all-or-nothing would leave a numeric *user* variable's slider undraggable.

## Consequences

- "Static" and "dynamic" stop being two concepts a user has to distinguish: one integration produces one
  catalog and appears once. Bounding the eager set becomes a contract rule that has to be enforced, where
  it was previously implied by which interface a provider chose — the host clamps to a per-provider
  maximum and logs, rather than refusing the provider outright and taking its whole variable surface
  offline for one tail-end mistake.
- The slider stops being a special case: its two-way binding becomes generic over any writable numeric
  variable, and any future control gets it without a second action contract. Removing the slider action
  contract also removes the situation where the read and write sides of one number are two unrelated
  declarations.
- Provider variables become writable from the UI, which is a genuine expansion of what a client can do to
  an integration. The capability declaration bounds it: a provider that declares nothing stays read-only.
- **An unavailable variable is now uniformly unresolvable in a condition, for every variable kind.** A
  condition against a polled integration variable that stopped updating previously fired against whatever
  stale value the registry held; it now stops firing. This is a behaviour change for existing
  integrations, not only new ones.
- A binding surviving a restart means a profile keeps referencing the same entity without the user
  re-picking it, even before the provider has finished reconnecting — it simply reads as unavailable.
- Every widget-facing and plugin-facing variable contract changed shape. This is a breaking change to the
  SDK and the protocol, acceptable only because nothing was released.
- **A user variable still has no unit**, so a History Graph bound to one renders none. Giving user
  variables an editable unit and semantic kind is the obvious closing move and is not done.
- **The template container has to be transparent on every conversion path Scriban has, not just the
  obvious two.** A numeric comparison that reads the container through integer conversion **truncates**
  rather than throwing, so a wrong answer looks like a right one; and because the container implements the
  script-object interface, Scriban asks it for a member before falling back to the default accessor, which
  silently swallowed everything that accessor served on a bare value. Both are covered by the overrides on
  `VariableTemplateContext` and by delegating unknown members back to the accessor. A future change there
  is a correctness change, not a refactor.
- **Usage-derived materialization — dropping the explicit bind step and deriving the working set from what
  templates and widgets reference — is deliberately not decided here.** It is the riskiest part and
  belongs in its own decision.
- **Attribute paths in conditions remain out of reach.** The condition evaluator resolves names against
  the entity on its own path and was not extended, so `x.unit` cannot be compared in a condition.
- Re-registration re-applies a provider's declaration onto an existing entity and publishes an update when
  it differs; without that, an attribute would be whatever it was the first time the host ever saw the
  variable.

## References

- [Issue #366](https://github.com/Macro-Deck-App/Macro-Deck/issues/366),
  [Issue #470](https://github.com/Macro-Deck-App/Macro-Deck/issues/470),
  [Issue #760](https://github.com/Macro-Deck-App/Macro-Deck/issues/760)
- [Variables](https://docs.macro-deck.app/features/variables/)
