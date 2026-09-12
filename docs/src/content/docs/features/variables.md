---
title: Variables
description: Declare variables with IVariableProvider - read-only and writable eager variables, attributes, testing, and the on-demand catalog.
---

An integration exposes variables by implementing `IVariableProvider`. For most plugins that means two
members: a list of `VariableDefinition`s and a `ReadAsync` that returns the current value of one of them.

## Quick start

```csharp
using MacroDeck.Sdk;
using MacroDeck.Sdk.Variables;

public sealed class MusicPlayerIntegration : IPluginIntegration, IVariableProvider
{
	private readonly PlaybackEngine _engine = new();

	public IReadOnlyList<VariableDefinition> Variables { get; } =
	[
		VariableDefinition.Eager("music_track", VariableType.Text) with { Id = "track" },
		VariableDefinition.Eager("music_is_playing", VariableType.Boolean) with { Id = "is-playing" },
		VariableDefinition.Eager("music_position", VariableType.Numeric, refreshInterval: TimeSpan.FromSeconds(1))
			with { Id = "position", Unit = "s", SemanticKind = VariableSemanticKinds.Duration }
	];

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(localId switch
		{
			"track" => VariableReading.Of(_engine.CurrentTrack.Title),
			"is-playing" => VariableReading.Of(_engine.IsPlaying),
			"position" => VariableReading.Of(_engine.PositionSeconds),
			_ => VariableReading.Unavailable
		});

	// IPluginIntegration members omitted.
}
```

The user now has `{{ vars.music_track }}`, `{{ vars.music_is_playing }}` and `{{ vars.music_position }}`.
The host calls `ReadAsync` for each variable on its own refresh interval - you never push eager values.

Three things to know:

- **`Name` is what the user types**, `music_track`. Lowercase, `[a-z0-9_]`, and prefix it with your
  plugin so it does not collide with another one.
- **`Id` is what `ReadAsync` receives** and what ends up in saved profiles. Leave it out and the host
  derives one from the name - but setting it explicitly keeps your `switch` readable and lets you rename
  the variable later without breaking anyone's configuration.
- **A value is a `string`, a number or a `bool`.** Anything else, and `VariableReading.Unavailable`, is
  shown as "not available" - use that for "no value right now" (not connected, not configured) rather
  than returning `""` or `0`.

## Defining a variable

`VariableDefinition.Eager(name, type, decimalPlaces, refreshInterval)` covers the common case; add
anything else with `with { ... }`.

| Property | What it does | Example |
| --- | --- | --- |
| `Name` | Variable name in templates. | `"weather_temperature"` |
| `Id` | Local id passed to `ReadAsync` / `SetValueAsync`. Stable once shipped. | `"temperature"` |
| `Type` | `Text`, `Numeric` or `Boolean`. | `VariableType.Numeric` |
| `DisplayName`, `Description` | Localized text shown in the variable picker. | `Strings.Variables.Temperature()` |
| `Unit` | Symbol shown next to the value, reachable as `vars.x.unit`. | `"°C"`, `"%"`, `"GB"` |
| `SemanticKind` | How the host formats it - see below. | `VariableSemanticKinds.Percentage` |
| `DecimalPlaces` | Digits shown for a numeric value. | `1` |
| `RefreshInterval` | How often the host calls `ReadAsync`. Host default when `null`. | `TimeSpan.FromSeconds(5)` |
| `Write` | Makes the variable writable - see [Writable variables](#writable-variables). | `new VariableWriteCapability()` |
| `Attributes` | Free-form strings, readable as `vars.x.<key>`. Not interpreted by the host. | `new Dictionary<string, string> { ["room"] = "office" }` |
| `Configuration` | Groups the variable under one configured instance (two OBS connections). | `new VariableConfiguration(entryId, "Studio PC")` |

`SemanticKind` tells the host how to render a number; `Unit` is what it shows next to it:

| `SemanticKind` | `Unit` | stored | rendered |
| --- | --- | --- | --- |
| `duration` | `s` | `187` | `03:07` |
| `percentage` | `%` | `12.5` | `12.5 %` |
| `bytes` | `B` | `1536` | `1.5 KB` |
| `none` | `fps` | `60` | `60 fps` |

An unknown kind renders as a plain number with its unit, so naming a newer one is never an error. Use
`bytes` only for a value that really is in bytes - a value already in GB is `none` with a `GB` unit.

A provider may declare at most `VariableLimits.MaxEagerVariablesPerProvider` (256) eager variables; the
host keeps the first 256 and logs an error. More than that belongs in [the catalog](#the-variable-catalog).

## Writable variables

A writable variable is how a **Slider widget** gets two-way binding: the slider writes through
`SetValueAsync` and reads the real value back through `ReadAsync`. Declare `Write` and implement
`SetValueAsync`:

```csharp
public IReadOnlyList<VariableDefinition> Variables { get; } =
[
	VariableDefinition.Eager("music_volume", VariableType.Numeric) with
	{
		Id = "volume",
		Unit = "%",
		SemanticKind = VariableSemanticKinds.Percentage,
		Write = new VariableWriteCapability()
	}
];

public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	=> ValueTask.FromResult(localId switch
	{
		// min, max and step give a bound Slider its range.
		"volume" => VariableReading.Of(_engine.VolumePercent, 0, 100, 1),
		_ => VariableReading.Unavailable
	});

public ValueTask<VariableWriteResult> SetValueAsync(string localId, object? value,
	CancellationToken cancellationToken = default)
{
	if (value is not (double or int or long))
	{
		return ValueTask.FromResult(VariableWriteResult.InvalidValue());
	}

	_engine.SetVolume((int)Convert.ToDouble(value, CultureInfo.InvariantCulture)); // clamps to 0-100
	return ValueTask.FromResult(VariableWriteResult.Applied());
}
```

- The host only calls `SetValueAsync` for a variable that declares `Write`; every other write is refused
  before it reaches you, so there is no need to check `localId` against a read-only list.
- `Applied` means you applied it. The host does not echo the requested value - the next `ReadAsync` is
  the truth, so clamping or rounding needs no extra work.
- The other results are `NotWritable`, `NotFound`, `Unavailable` (can write, just not right now - e.g.
  disconnected), `InvalidValue` and `Failed`. Never declare `Write` and then answer `NotWritable`: that is
  a slider that silently does nothing, and conformance check [MDC0314](/reference/conformance/) fails it.
- Set `Write = new VariableWriteCapability { CommitOnRelease = true }` when every intermediate value of a
  drag would be disruptive (seeking a track). Leave it off for volume, where live feedback is the point.

`Min`, `Max` and `Step` come from the reading rather than the definition because they can change - a
seek bar's maximum is the current track's length.

## Variables that depend on configuration

`Variables` may change with configuration. After the configuration changed, tell the host to read it
again with `CatalogChanged(CapabilityKinds.Variables)` on an injected `IPluginCatalogNotifier` - the
[weather sample](/introduction/samples-and-template/) does this after its config flow.

`DeclaredVariables` is what the host shows for an integration that is not configured yet. It defaults to
`Variables`; override it only when `Variables` is empty until something is configured, and set
`VariablesDependOnConfiguration` when its names contain a `VariableNameTemplate` placeholder for a
per-instance segment.

## Testing

`PluginTestHarness` reads and writes variables the way the host does:

```csharp
await using var harness = /* your harness setup */;
await harness.InitializeIntegrationsAsync();

var track = (await harness.Variables.GetAsync("track")).DataAs<VariableReadingDto>();
Assert.That(track!.Value.Text, Is.EqualTo("Intro"));

var written = (await harness.Variables.SetAsync("volume",
	new VariableValueDto { Kind = "number", Number = 35 })).DataAs<VariableSetResult>();
Assert.That(written!.Status, Is.EqualTo("Applied"));
```

`GetAsync` and `SetAsync` take the local id, not the name. The
[sample plugins](/introduction/samples-and-template/) test every variable they declare this way.

## Templates

A variable is `{{ vars.<name> }}` in any template; its static attributes are suffixes on the same
reference: `{{ vars.cpu.unit }}`, `{{ vars.room_sensor.room }}`.

`vars.<name>.state` is computed by the host and tells an unavailable variable apart from an empty one:

| member | true when |
| --- | --- |
| `state.is_available` | the reference resolved to a value |
| `state.is_not_available` | it did not - unknown name, or a provider that went quiet |
| `state.is_empty` | it resolved **and** renders as zero characters |
| `state.is_not_empty` | it resolved **and** renders as at least one character |

```liquid
{% if vars.music_artist.state.is_not_empty %}By {{ vars.music_artist }}{% endif %}
```

Because `state` is resolved first, an `Attributes` key named `state` is unreachable. `state` works on
`vars` references only, not on `event` parameters or script inputs.

## The variable catalog

Use the catalog when your variables are a runtime resource space too large to declare up front - Home
Assistant entities, OBS sources, MQTT topics. Nothing is registered until the user picks a resource in
the variable browser; only then does it become an ordinary `{{ vars.name }}` variable. A provider can
have eager variables and a catalog at the same time.

```csharp
public sealed class Foobar2000Integration : IPluginIntegration, IVariableProvider
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

		// The client's cursor is handed straight through as the continuation token.
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

	// A tag typed by hand or read from an old profile is still valid - resolve it.
	public ValueTask<VariableDefinition?> ResolveAsync(
		string localId,
		CancellationToken cancellationToken = default)
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

### Catalog rules

- **Ids.** A catalog id may contain anything except `::`, whitespace and control characters, up to
  `MacroDeckId.MaxResourceLocalIdLength` - `sensor.office_temperature` and GUIDs are fine. Encode names
  with spaces (`Main Camera` → `Main_Camera`) and decode them in `ResolveAsync`; an item with an invalid
  `Id` is dropped. Ids are local: the host adds and strips your integration's prefix.
- **Paging.** Return one page per `DiscoverAsync` call, at most `MaxVariableCatalogPageSize` items, and
  put your source's own cursor in `ContinuationToken`. It is opaque and never persisted.
- **Hierarchy.** `query.ParentId` is `null` for the roots, otherwise the node being opened. Set
  `IsContainer` on nodes with children and `IsBindable = false` on pure grouping nodes. A flat provider
  ignores `ParentId`.
- **Search.** Leave `SupportsSearch` off unless you honor `query.Search`; the host then shows no search
  box rather than filtering a single page.
- **`CatalogEntryCount`.** Return a total only when it is cheap; `null` otherwise.
- **`ResolveAsync` returns `null` only for an invalid id.** A resource that is merely gone right now (an
  unplugged device, a disconnected integration) must still resolve: the binding then shows as unavailable
  and resumes on its own, while `null` makes it a broken reference the user has to fix. A plugin that is
  offline appears as unresolvable until it reconnects, whatever your code returns.

### Push instead of poll

By default the host polls every bound resource with `ReadAsync`. A provider backed by an event stream
sets `SupportsPush => true` and publishes instead:

1. `OnAttachedAsync(sink)` hands you an `IVariableSink` once (only when both `SupportsCatalog` and
   `SupportsPush` are `true`).
2. `SubscribeAsync(localIds)` is called with the **complete** set of bound ids every time it changes (an
   empty set means "watch nothing"). Return the current values you already have, or an empty list.
3. Call `sink.PublishAsync` with values for ids in the latest set; others are dropped. Call
   `InvalidateCatalogAsync` when the set of resources itself changed.

Push applies to the catalog only - eager variables are always polled.

### Resource lifetime

A resource that disappears keeps its binding and variable; it just reads as unavailable and resumes
without re-binding once it can be resolved and read again.

## Over the plugin protocol

Each **eager** variable is declared as one capability, like an action. Catalog ids are never declared -
they travel in the operation arguments, which keeps a large catalog under `MaxDeclaredCapabilities`. See
[the WebSocket reference](/reference/websocket/#capabilities) for the
`describe`/`get`/`set`/`discover`/`resolve`/`subscribe` operations and the `variable-values` host API.
Declare `host:variable-values` in [`manifest.json`](/reference/manifest/) when you use it.
`MacroDeckTestHost` drives all six operations; see [conformance](/reference/conformance/) for MDC0311, MDC0314
and MDC0315.

## Writing a user variable

To write a variable the **user** owns instead of declaring your own, use the user-variable API on
`IIntegrationContext`: `CreateAsync` creates one, `ApplyAsync` changes an existing one. Pass an owner
widget id (from `ActionExecutionContext.OwnerWidgetId`) to make it local to that widget, where it shadows
a global of the same name; the host refuses an unknown widget id.

`ApplyAsync`'s `Set` also works on provider variables that declare `Write`; `NotEditable` for the rest.
`Add`, `Toggle` and `Append` are user-variable only, because they compute from the last value the host
saw. `Unavailable` means the owner accepts writes but could not take this one - retry later.
