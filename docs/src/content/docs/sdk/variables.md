---
title: Variables
description: One provider catalog of variable definitions - eager and on-demand materialization, attributes, the write capability, discovery and binding.
---

`IVariableProvider` is one catalog of variable definitions, addressable by definition id. A definition
declares what the value *is* - its type, its unit, what it means - and, optionally, that the owner
accepts writes to it. Every provider call should stay bounded and cancellable.

Each definition declares when it comes into existence, and the host validates that declaration against
the surface it arrived on rather than inferring it:

- `VariableMaterialization.Eager` - registered and polled from the moment the provider is initialized.
  These are the definitions `Variables` and `DeclaredVariables` return, and `ReadAsync` is called for
  each of them on its own refresh interval.
- `VariableMaterialization.OnDemand` - present in the catalog, but a registry entry only once a user
  binds it. These are the definitions `DiscoverAsync` and `ResolveAsync` return.

`Variables` is the runtime set, per configuration. `DeclaredVariables` is what the integration offers
*before* it is configured, so a client can show a catalog for an unconfigured integration; leave it alone
unless the two differ, in which case set `VariablesDependOnConfiguration`. A declared name may be a
`VariableNameTemplate` - a name with a placeholder the host fills in once per configuration entry - and
such a definition resolves to no id at all until that happens, which is why `ResolvedId` is nullable and
why the pre-configuration catalog is the one place a null one is kept rather than dropped.

**The eager set is bounded by contract at `VariableLimits.MaxEagerVariablesPerProvider` (256).** An
eager variable is registered and polled for as long as the integration is enabled and appears in every
consumer's candidate list forever, so the set has to stay small enough for a person to read; a provider
with more to offer exposes the rest through the catalog instead. Exceeding the bound is never a
registration failure - the host keeps the first 256 in declaration order, drops the rest and logs an
error - because one tail-end mistake must not take an integration's whole variable surface offline.
Conformance check [MDC0315](/sdk/conformance/) holds a plugin talking the wire protocol directly to the
same number.

A provider whose set is small and known ahead of time - a handful of sensors, a fixed set of player
states - declares it eagerly and implements nothing else. A provider whose set is a runtime resource
space that cannot reasonably be declared up front - Home Assistant entities and their attributes, ADB
devices, OBS scenes and sources, MQTT topics, foobar2000 custom tags - sets `SupportsCatalog` and serves
[the catalog half](#the-variable-catalog). One provider may do both, and Meld does: eight eager
variables next to a browsable per-track catalog.

`SupportsCatalog` is off by default, and that default is the whole behaviour-preservation story: a
provider that leaves it alone is never asked to discover, never offers a browse tree and never appears
as a catalog source.

## Reading a value

`ReadAsync` takes a definition's local id - `ResolvedId` for an eager definition, the resource id for an
on-demand one - and returns a `VariableReading`: a value plus the volatile attributes that came back
with it. Return `VariableReading.Unavailable` for "not right now"; it is a distinct answer from a value,
and the host marks the variable unavailable rather than storing a null. A value is one of three shapes:
a string, a number, or a bool.

## Attributes

Attributes are a typed core the host acts on plus an open map it does not, split by how often they can
change.

**Static attributes** are declared once with the definition and cached with the variable:
`DisplayName`, `Description`, `Unit`, `SemanticKind` and `DecimalPlaces`, plus the free-form
`Attributes` map. `SemanticKind` answers "what does this value mean", where `Type` answers "how is it
stored and compared":

| `Type` | `SemanticKind` | `Unit` | stored | rendered |
| --- | --- | --- | --- | --- |
| `Numeric` | `duration` | `s` | `187` | `03:07` |
| `Numeric` | `percentage` | `%` | `12.5` | `12.5 %` |
| `Numeric` | `bytes` | `B` | `1536` | `1.5 KB` |
| `Numeric` | `none` | `fps` | `60` | `60 fps` |

`VariableSemanticKinds` names the kinds the host formats, but the vocabulary is **open**: a kind the
host does not know renders as a plain number with the declared unit, which is also what a host built
before that kind existed does. Naming one is therefore never an error and never needs a protocol change.
Declare `bytes` only for a value that really is in bytes - a reading already scaled to GB is `none` with
a `GB` unit.

The `Attributes` map is deliberately uninterpreted: it is readable from a template and nothing else.
Anything the host must act on belongs in the typed core. One name is reserved and cannot be used by an
attribute: `state`, described below. It is clamped to
`VariableLimits.MaxAttributeEntries` entries of at most `VariableLimits.MaxAttributeValueLength`
characters each, because the map is broadcast to every connected client alongside the value.

**Volatile attributes** - `Min`, `Max` and `Step` - travel with the reading instead, because they can
change with every one. A media player's seek range is its current track's length; an OBS input
gain-boosted in Advanced Audio Properties reports a maximum above 100. Return them from `ReadAsync`
through `VariableReading.Of(value, min, max, step)`; there is deliberately nowhere to declare them on
the definition.

Attributes reach a template as a suffix on the same reference the value uses: `{{ vars.cpu }}` is still
the number, and `{{ vars.cpu.unit }}` is `%`. The typed core is what the host itself formats with, and it
formats per reader - a History Graph's axis unit and a Slider's readout resolve in the language of
whoever is looking - so declaring a unit and a semantic kind once is what stops each widget from being
told again.

## Reading a variable's state from a template

`vars.<name>.state` is host-computed and answers before the `Attributes` map, so an attribute called
`state` is unreachable rather than merely shadowed. It carries four booleans, matching the condition
operators of the same names:

| member | true when |
| --- | --- |
| `state.is_available` | the reference resolved to a value |
| `state.is_not_available` | it did not resolve - an unknown name, or a provider that has gone quiet or stale |
| `state.is_empty` | it resolved **and** renders as zero characters |
| `state.is_not_empty` | it resolved **and** renders as at least one character |

`is_empty` and `is_not_empty` are not negations of each other: a reference that did not resolve is
neither, because only a value that exists can be said to be empty. This is what lets a template tell an
unavailable variable apart from a blank one, which the rendered text alone cannot - an unavailable
reference renders as a placeholder.

```liquid
{% if vars.artist.state.is_not_empty %}By {{ vars.artist }}{% endif %}
```

`state` is available on `vars` references only, not on `event` parameters or script inputs.

## Writing a variable

A definition carrying a `Write` capability declares that its owner accepts `SetValueAsync` for it. The
declaration is separate from the implementation so a client can decide *before* a write whether to offer
a control at all, the same way `IsBindable` gates binding; the host refuses a write to a definition that
declares none without ever asking the provider.

```csharp
Write = new VariableWriteCapability { CommitOnRelease = true }
```

Set `CommitOnRelease` when every intermediate value of a drag is a disruptive side effect of its own -
seeking a track jumps the audio - and leave it off for something like volume, where live feedback during
the drag is the point.

A capability takes no parameters beyond the value: the variable *is* the bound target, so there is
nothing else to configure. `SetValueAsync` answers with a `VariableWriteResult` whose `VariableWriteStatus`
is `Applied`, `NotWritable`, `NotFound`, `Unavailable`, `InvalidValue` or `Failed`. A refused write is a
status, not a failed operation.

`Applied` means the provider applied the value, **not** that the host has seen the result - the
authoritative value still arrives on the read side, so a provider need not echo it. A provider that can
only sometimes write still declares the capability and refuses the individual write with `Unavailable`;
declaring `Write` and then answering `NotWritable` leaves a control the user can drag that silently does
nothing, which conformance check [MDC0314](/sdk/conformance/) fails.

## The variable catalog

Set `SupportsCatalog` and give the catalog a `CatalogName`, and the provider gains a browsable,
pageable resource tree on top of its eager set.

```csharp
public sealed class Foobar2000Integration : IIntegration, IVariableProvider
{
	private readonly Foobar2000Client _client;

	public IReadOnlyList<VariableDefinition> Variables { get; } = [];

	public bool SupportsCatalog => true;

	public bool SupportsSearch => true;

	public string CatalogName => "foobar2000";

	public async ValueTask<VariableCatalogPage> DiscoverAsync(
		VariableCatalogQuery query,
		CancellationToken cancellationToken = default)
	{
		if (!_client.IsConnected)
		{
			return VariableCatalogPage.Empty;
		}

		// The client's own cursor is opaque to us too - it is handed straight through as
		// VariableCatalogPage.ContinuationToken, never parsed as an offset.
		var page = await _client.GetCustomTagsAsync(query.Search, query.PageSize, query.ContinuationToken,
			cancellationToken);

		return new VariableCatalogPage
		{
			Items = page.Tags
				.Select(tag => VariableDefinition.OnDemand(tag.Name, VariableType.Text) with
				{
					Name = $"foobar_{tag.Name}",
				})
				.ToList(),
			ContinuationToken = page.NextCursor,
		};
	}

	public ValueTask<VariableDefinition?> ResolveAsync(
		string localId,
		CancellationToken cancellationToken = default)
		// A tag the user never saw in DiscoverAsync - typed by hand or read back out of a profile
		// saved months ago - is still a legal tag name. Resolve it rather than rejecting it.
		=> ValueTask.FromResult(Foobar2000Tags.IsValidName(localId)
			? VariableDefinition.OnDemand(localId, VariableType.Text)
			: null);

	public async ValueTask<VariableReading> ReadAsync(
		string localId,
		CancellationToken cancellationToken = default)
		=> _client.IsConnected
			? VariableReading.Of(await _client.GetCustomTagValueAsync(localId, cancellationToken))
			: VariableReading.Unavailable;
}
```

### Resource ids are local and provider-owned

Every id crossing the catalog half is a *local* resource id - the part after `::`. The host qualifies it
with the owning integration's id before it reaches persisted configuration, and strips the qualifier
again before calling back in, so a provider never parses or produces its own integration id.

A catalog id is held to `LocalIdKind.Resource`, not the `LocalIdKind.Declared` an eager definition's id
follows: any characters except the reserved `::` separator, whitespace and control characters, up to
`MacroDeckId.MaxResourceLocalIdLength`. `sensor.office_temperature` and a GUID are both legal, because
these ids come out of someone else's namespace rather than out of your source code. Whitespace is still
rejected, though: a source name like "Main Camera" from an upstream API that allows spaces is not a legal
id as-is, and `ResolveAsync`, `ReadAsync` and `SubscribeAsync` for it will simply never be called -
`MacroDeckId.IsValidLocalId` rejects the id before a bind is even attempted, and `DiscoverAsync` results
with an invalid `Id` are dropped rather than offered for binding. Encode such a name into a legal id and
reverse the encoding to look it back up; substituting each whitespace run with `_` is enough as long as
the result stays unambiguous for your resource set, since only your own definitions have to agree with
your own `ResolveAsync`.

### Hierarchy and paging

`DiscoverAsync` returns one page at a time, bounded by the protocol's `MaxVariableCatalogPageSize`. The
host calls it only for the resource browser - never to build a working set - so return whatever slice is
cheap to produce and leave the rest behind `VariableCatalogPage.ContinuationToken`.

The token is an **opaque continuation**, not an offset or a limit, because a catalog mutates while the
user is browsing it: an offset over a set that gained or lost an entry silently skips or duplicates
items, while a token lets each provider carry whatever cursor its own source actually has. It is never
persisted - it only has to survive the browsing session it was issued in.

`VariableCatalogQuery.ParentId` walks the hierarchy: `null` asks for the roots, any other value asks for
that node's children. A flat provider ignores it and returns everything at the root.
`VariableDefinition.IsContainer` tells the browser a node has children worth asking for, so opening a
leaf never has to probe speculatively; `IsBindable` says whether the node itself carries a value - a Home
Assistant entity is commonly both a container (its attributes) and bindable (its state).

`SupportsSearch = false` (the default) makes the host skip offering a search box for this provider
entirely, rather than filtering one returned page locally - filtering a page out of a set the provider is
itself paging would silently hide most matches.

### Resolving an id that was never enumerated

A resource id does not have to have come out of `DiscoverAsync`. A user typing a foobar2000 custom tag
name the provider has no way to enumerate up front, or an id read back out of a profile saved months ago,
still has to resolve through `ResolveAsync`.

`ResolveAsync` returning `null` means the id is **invalid**, not merely absent. A resource that is simply
gone right now - an unplugged ADB device, a deleted OBS source, an integration that is not currently
connected - must still resolve, because the host treats the two very differently: an unresolvable id is a
broken reference the user has to fix, while a resolvable one whose value happens to read unavailable
resumes on its own when the resource comes back. When a provider genuinely cannot tell the two apart,
resolve the id - a binding that survives a restart is worth more than an early error.

This is the contract your own `ResolveAsync` must honor. It is not, however, what a caller sees when the
provider is out-of-process and unreachable: the host collapses any transport failure while calling a
disconnected plugin to the same `null` `ResolveAsync` returns for an invalid id, so a plugin that is
merely offline still surfaces as an unresolvable reference rather than a resolvable-but-unavailable one
until it reconnects.

### Push or poll

`SupportsPush` is read once, when the host describes the provider.

- `SupportsPush = false` (the default) makes the host poll every bound resource with `ReadAsync` on its
  refresh interval. Correct for anything without an event stream, but linear in the number of bindings.
- `SupportsPush = true` tells the host this provider delivers values through `IVariableSink` instead. It
  is a property of the provider, not of a connection - a push provider whose upstream is momentarily down
  still reports `true` and reports its resources as unavailable rather than falling back to polling. The
  host hands the sink to `OnAttachedAsync`, once, and only for a provider that reports both
  `SupportsCatalog` and `SupportsPush`.

Push covers the catalog half only. A provider that declares both halves keeps having its eager set polled
on each definition's own refresh interval, whatever `SupportsPush` says.

A push provider calls `IVariableSink.PublishAsync` with data-carrying values, not an
invalidate-then-reread signal - the host never issues a follow-up read for a pushed id. A provider may
only publish ids that were in its **most recently subscribed** set; anything else is dropped. Only
resources the user has actually bound are ever subscribed to - discovery never causes a subscription on
its own. `InvalidateCatalogAsync` is the separate, data-free signal that the enumerable set itself
changed.

`SubscribeAsync` replaces the provider's working set wholesale, not incrementally: the host calls it with
the complete set of ids it currently cares about every time that set changes, and an empty set is a
legitimate call meaning "watch nothing". The returned values let a newly bound resource show something
immediately instead of costing a second round trip; a provider with nothing cached yet may return an
empty list, and the host falls back to `ReadAsync`.

### Binding and resource lifetime

The user picks a resource in the variable browser, and the host turns it into an ordinary variable at that
moment. Templates, conditions and widget bindings keep using `{{ vars.name }}` exactly as they always have
and never carry a qualified resource id - binding is the one place the catalog and the ordinary variable
worlds meet.

A resource that disappears - a device unplugged, an entity removed upstream - leaves the binding and the
variable in place, merely unavailable, rather than deleting either. It resumes automatically, with no
re-binding step, the moment the provider can resolve and read it again.

### Variables over the plugin protocol

A plugin declares one capability per **eager** variable, exactly as `actions` declares one per action.
Catalog resource ids are never declared: they travel inside each operation's own arguments, which is what
keeps a provider with tens of thousands of entities well under `MaxDeclaredCapabilities`. See
[the WebSocket reference](/reference/websocket/#capabilities) for the
`describe`/`get`/`set`/`discover`/`resolve`/`subscribe` operations and the `variable-values` host API's
`value`/`invalidate` callbacks.

Declare `host:variable-values` in `manifest.json` alongside the other host APIs your plugin uses - it
mirrors that host API the same way `host:devices` mirrors the `devices` host API. See
[the manifest reference](/reference/manifest/).

`MacroDeckTestHost` drives all six operations against a plugin under test, the same way it drives any
other capability. See [conformance](/sdk/conformance/) for MDC0311, MDC0314 and MDC0315.

## The user-variable API

Use the user-variable API on `IIntegrationContext` when an integration needs to write a variable owned by the user rather than publishing its own provider variable. It both creates and writes: `CreateAsync` brings a user variable into being, `ApplyAsync` changes one that already exists.

Both take an optional owner widget id. Without one the variable is global; with one it belongs to that widget and shadows a global of the same name whenever a flow of that widget resolves it — which is how a widget exposes local state such as a current track without taking the name globally. A flow that belongs to a widget gets the id from `ActionExecutionContext.OwnerWidgetId`. The host checks that the widget exists, so a widget id that is wrong or stale is refused rather than creating a variable nothing can reach.

`ApplyAsync`'s `Set` reaches any variable whose owner accepts a write, not only a user-owned one - a provider variable declaring a write capability included, with `NotEditable` for the rest. `Add`, `Toggle` and `Append` stay user-only: they compute from the value the host last saw, so against a variable whose owner has not refreshed it they would silently lose increments. `Unavailable` is the status for an owner that accepts writes but could not take this one right now; unlike a plain failure, a later retry may succeed.
