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
| `Type` | `Text`, `Numeric`, `Boolean` or `Color` - see [Color variables](#color-variables). | `VariableType.Numeric` |
| `DisplayName`, `Description` | Localized text shown in the variable picker. | `Strings.Variables.Temperature()` |
| `Unit` | Symbol shown next to the value, reachable as `vars.x.unit`. | `"°C"`, `"%"`, `"GB"` |
| `SemanticKind` | How the host formats it - see below. | `VariableSemanticKinds.Percentage` |
| `DecimalPlaces` | Digits shown for a numeric value. | `1` |
| `RefreshInterval` | How often the host calls `ReadAsync`. Host default when `null`. | `TimeSpan.FromSeconds(5)` |
| `Write` | Makes the variable writable - see [Writable variables](#writable-variables). | `new VariableWriteCapability()` |
| `Attributes` | Free-form strings, readable as `vars.x.<key>`. Not interpreted by the host. | `new Dictionary<string, string> { ["room"] = "office" }` |
| `Configuration` | Groups the variable under one configured instance (two OBS connections). | `new VariableConfiguration(entryId, "Studio PC")` |
| `IsShareable` | `false` keeps the variable on this Macro Deck: the user cannot share it with other Macro Decks, and one shared earlier is no longer served. Defaults to `true`. | `false` for a variable that mirrors another Macro Deck |

`SemanticKind` tells the host how to render a number; `Unit` is what it shows next to it:

| `SemanticKind` | `Unit` | stored | rendered |
| --- | --- | --- | --- |
| `duration` | `s` | `187` | `03:07` |
| `percentage` | `%` | `12.5` | `12.5 %` |
| `bytes` | `B` | `1536` | `1.5 KB` |
| `bytesPerSecond` | `B/s` | `3670016` | `3.5 MB/s` |
| `none` | `fps` | `60` | `60 fps` |

An unknown kind renders as a plain number with its unit, so naming a newer one is never an error. A host
built before `bytesPerSecond` existed shows such a value as `3670016 B/s`, which is why the unit is still
declared. Use
`bytes` only for a value that really is in bytes - a value already in GB is `none` with a `GB` unit.

A provider may declare at most `VariableLimits.MaxEagerVariablesPerProvider` (256) eager variables; the
host keeps the first 256 and logs an error. More than that belongs in [the catalog](#the-variable-catalog).

## Color variables

`VariableType.Color` holds a color. Its value is text, lowercase `#rrggbb` when opaque or `#rrggbbaa` with
alpha: return it from `ReadAsync` as a string, and expect one in `SetValueAsync`. The host also accepts
`#rgb`, `#rgba`, `rgb(...)` and `rgba(...)` from you and stores the canonical form; anything else is
invalid. Over the protocol the value travels as a `text` value, like any string.

Users can bind a widget's colors, folder and profile backgrounds, the accent color and action color
parameters to a Color variable. See [Colors from a variable](/guide/tips/#colors-from-a-variable) for
what they see.

A Macro Deck release from before `Color` drops a definition that declares it, with the rest of your
variables unaffected.

A script input can be a color too, `ScriptInputType.Color` with a value in the same format. A plugin built
on this SDK announces that it knows the type and sees such an input as `color`; an older plugin sees it as
`text` carrying the hex value.

### Resolving colours yourself

Two places hand you colours already resolved: a `Color` [action parameter](/features/actions/) when the
action runs, and the widget data Macro Deck gives your widget's session. Anywhere else a colour may be a
reference string such as `{{ vars.primary | color | color_darken: 20 }}`: your own settings, a config or
setup flow, or a view of your own with
[`AllowVariables`](/ui/views/widget-configuration/#offering-a-colour-variable). Resolve those through
`IIntegrationContext.Colors`, an `IColorApi`:

```csharp
var color = await context.Colors.ResolveAsync(settings.LightColor) ?? "#ffffff";

_colorWatch = await context.Colors.WatchAsync(settings.LightColor, async (color, ct) =>
	await _lights.SetColorAsync(color ?? "#ffffff", ct));

// later, to stop:
await _colorWatch.DisposeAsync();
```

- Both take a fixed colour or a reference and return lowercase `#rrggbb` or `#rrggbbaa`, or `null` for "no
  colour, use your default": the variable is missing, unavailable or not a `Color`. Pass a `widgetId` to let
  that widget's own variables shadow global ones; an unknown widget gives `null`.
- `ColorReference.IsReference(value)` tells a reference from a fixed colour without asking the host.
- A watch's callback receives the current colour once, then again only when it changes, including to
  `null`. Watches survive reconnects and are released when the integration is shut down or initialized
  again; dispose one to stop it earlier. Callbacks run on thread-pool threads.
- A plugin may hold at most `ProtocolLimits.MaxColorWatches` (1024) watches across its integrations;
  `WatchAsync` throws `InvalidOperationException` above that.
- A reference is at most `ProtocolLimits.MaxColorReferenceLength` (1024) characters with at most
  `MaxColorReferenceSteps` (32) modifiers. A longer value is not a reference.
- On a Macro Deck release without colour resolution, a fixed colour still resolves locally, a reference
  gives `null`, and a watch delivers its value once.

In tests, `PluginTestHarness`'s `Context.Colors` is a `FakeColorApi`: seed a value with `Set(value, color)`,
which also notifies matching watches, and check `ActiveWatchCount`. Over the wire, `MacroDeckTestHost.Colors`
does the same with `SetAsync`, and `WatchesOf(pluginId)` lists a plugin's watches. Unseeded, a fixed colour
resolves to its canonical form and a reference to `null`.

```csharp
await harness.Context.Colors.Set("{{ vars.primary | color }}", "#3366ff");
```

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

`Step` is the slider's default grid, not a guarantee: a user can give a bound slider a custom step, so
`SetValueAsync` may receive a value that is not a multiple of your step. It is always within `Min` and
`Max`. Round or reject it the way your target needs.

## Variables that depend on configuration

`Variables` may change with configuration. After the configuration changed, tell the host to read it
again with `context.Variables.NotifyDeclarationsChangedAsync()`. For a plugin this sends the same
`state.update` for the `variables` kind that `CatalogChanged(CapabilityKinds.Variables)` on an injected
`IPluginCatalogNotifier` sends, so it works with every Macro Deck version - the
[weather sample](/introduction/samples-and-template/) does this after its config flow. It is about the
declared eager set only; a catalog announces a changed set of resources with `InvalidateCatalogAsync`
(see [Push instead of poll](#push-instead-of-poll)).

## Reading again now

Macro Deck reads each eager variable at its `RefreshInterval`. When something changed what `ReadAsync`
would answer - a connection came up, a device reported new state - ask for every declared variable to be
read on the next poll instead:

```csharp
await context.Variables.RequestRefreshAsync(cancellationToken);
```

Requests made while one is still on its way are merged into one, so calling it from every callback of an
event stream is fine. It is best effort: it never throws because the request could not be delivered, and a
Macro Deck that predates it treats it as a no-op, so the variables are still read at their interval.
Pushed catalog resources are unaffected; publish those through the sink.

When a change affects only a few variables, name them by definition id so the rest keep their cadence:

```csharp
await context.Variables.RequestDefinitionRefreshAsync(["volume-percent", "muted"], cancellationToken);
```

The ids can also name catalog resources a user bound, so a catalog provider that polls (`SupportsPush` is
`false`) can turn an event from its service into an immediate read of only the affected resources:

```csharp
// The service reported that the microphone input was muted.
await context.Variables.RequestDefinitionRefreshAsync([$"{connectionId}/input/mic/muted"], cancellationToken);
```

Ids that match neither a declared variable nor a bound catalog resource are ignored, and an empty list asks for
nothing. Reading more than asked for is always allowed: a pending request for every declared variable covers
pending requests for declared ones, while named catalog resources are still sent next to it. An `IVariableApi`
that does not override the member, or a Macro Deck that predates it, reads every declared variable instead. An
older Macro Deck ignores catalog resource ids, so those resources keep their refresh interval there. Pushed
catalog resources are unaffected; publish those through the sink.

## Names

A canonical variable name matches `^[a-z][a-z0-9_]*$`. `VariableNames.IsValid` checks one, and
`VariableNames.Sanitize` derives one from any text the way Macro Deck does: `vars.` is dropped, accents are
removed, letters are lowercased, every other character becomes `_`, runs of `_` collapse, and a leading digit
gets a `v_` prefix. The same text always gives the same name, so a name derived from a stable label stays
stable:

```csharp
var name = VariableNames.Sanitize($"{machineName} fps"); // "gaming_pc_fps"
```

`DeclaredVariables` is what the host shows for an integration that is not configured yet. It defaults to
`Variables`; override it only when `Variables` is empty until something is configured, and set
`VariablesDependOnConfiguration` when its names contain a `VariableNameTemplate` placeholder for a
per-instance segment.

## History of your own variables

```csharp
await using var window = await context.Variables.OpenHistoryAsync("weather_berlin_temperature", 40, cancellationToken);
window.Changed += (_, _) => Redraw(window.Values);
```

`OpenHistoryAsync` opens a rolling window on one of your own global numeric variables, for example to draw a
graph in a widget. Macro Deck samples the variable once per second while a window on it is open, so the
values are evenly spaced and the graph advances whether the value changes or not. `Values` holds at most the
requested number of samples, oldest first, and is empty until the variable first reads as a number. The
window may be opened before the variable exists. Dispose it when the graph is no longer shown.

A name that belongs to someone else throws a `VariableHistoryException` with `NotOwned`, and too many open
windows `LimitExceeded`. A Macro Deck that predates variable history, or an `IVariableApi` that does not
implement it, throws `Unsupported`; catch it and draw without a graph.

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

`FakeIntegrationContext.Variables` counts `RequestRefreshAsync` and `NotifyDeclarationsChangedAsync` calls in
`RefreshRequests` and `DeclarationChanges`, lists the ids of each `RequestDefinitionRefreshAsync` call in
`DefinitionRefreshRequests`, and keeps what `BindAsync` recorded in `Bindings`. `RecordSample` adds a sample to every history window open on a
name, and `OpenHistoryWindows` counts the windows that are not disposed yet.

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

Color filters derive a shade from a color. The value piped in must be a `Color` variable or a color
literal; a `Text` variable is refused even when it holds a hex value. `color` reads the value as it is, and
the result of every filter is `#rrggbb` or `#rrggbbaa`:

```liquid
{{ vars.primary | color | color_darken: 20 | color_opacity: 70 }}
{{ "#ff0000" | color_darken: 10 }}
```

| filter | argument |
| --- | --- |
| `color_lighten`, `color_darken` | percent of lightness to add or remove |
| `color_saturate`, `color_desaturate` | percent of saturation to add or remove |
| `color_opacity` | the alpha to set, in percent |
| `color_increase_opacity`, `color_reduce_opacity` | percent to move the alpha toward opaque, or to reduce it by |
| `color_hue` | degrees to rotate the hue |
| `color_mix` | another color (`"#ffffff"` or a Color variable such as `vars.other`) and the percent of it to blend in |

A missing, unavailable or invalid color renders as an empty string, which a color field treats as unset.

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
- **`CatalogEntryCount`.** Return a total only when it is cheap; `null` otherwise. Without a total the
  host shows how many entries it has loaded so far, or no count at all when `SupportsSearch` is on,
  because a searchable catalog is only loaded as the user expands or searches it.
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

### Recording bindings yourself

The user binds catalog resources in the variables view. An integration can record bindings itself, for
example to carry variables over from an older version that created them another way:

```csharp
var outcomes = await context.Variables.BindAsync(
[
	new VariableBindingRequest("entity/light.kitchen/state", "ha_light_kitchen", VariableType.Text)
	{
		MigratedFrom = "my-plugin/watched-entities-v1"
	}
], cancellationToken);
```

- The bindings always belong to the calling integration, and are recorded together or not at all.
- A resource the integration already has a binding for answers `AlreadyBound` and keeps its binding, even
  when the user renamed its variable since. Running the same migration twice is therefore safe.
- Every name must satisfy `VariableNames.IsValid` and every resource id must be a valid resource id;
  otherwise `ArgumentException` is thrown and nothing is recorded. A plugin sends at most 512 per call.
- Recording does not create the variables right away. Macro Deck creates them when it restores bindings,
  at the latest on its next start; a requested name another variable holds by then is replaced with one
  derived from it, as for any binding.
- `InvalidOperationException` means the bindings could not be stored. The default implementation, and a
  Macro Deck that predates the member, throw `NotSupportedException`. Keep the data you migrate from until a
  call succeeds, so the next start can try again.

## Over the plugin protocol

Each **eager** variable is declared as one capability, like an action. Catalog ids are never declared -
they travel in the operation arguments, which keeps a large catalog under `MaxDeclaredCapabilities`. A
provider with only a catalog or only on-demand variables therefore declares nothing; Macro Deck asks it for
`describe` when it connects and offers its variables from then on, so it needs no announcement. See
[the WebSocket reference](/reference/websocket/#capabilities) for the
`describe`/`get`/`set`/`discover`/`resolve`/`subscribe` operations, the `variables`/`refresh` host operation
behind `RequestRefreshAsync` and `RequestDefinitionRefreshAsync` (with an optional `definitionIds` list), the
`variables`/`bind` operation behind `BindAsync`, the `variables`/`history.open`, `history.read` and `history.close` operations
behind `OpenHistoryAsync` (a plugin's window reads its samples once per second and ends with the
connection), and the `variable-values` host API.
Declare `host:variable-values` in [`manifest.json`](/reference/manifest/) when you use it.
`MacroDeckTestHost` drives all six operations; see [conformance](/reference/conformance/) for MDC0311, MDC0314,
MDC0315 and MDC0403, which also check a plugin whose variables come only from a catalog or on demand.

## Writing a user variable

To write a variable the **user** owns instead of declaring your own, use the user-variable API on
`IIntegrationContext`: `CreateAsync` creates one, `ApplyAsync` changes an existing one. Pass an owner
widget id (from `ActionExecutionContext.OwnerWidgetId`) to make it local to that widget, where it shadows
a global of the same name; the host refuses an unknown widget id.

`ApplyAsync`'s `Set` also works on provider variables that declare `Write`; `NotEditable` for the rest.
`Add`, `Toggle` and `Append` are user-variable only, because they compute from the last value the host
saw. A `Color` user variable takes `Set` only, with a value in the formats above. `Unavailable` means the owner accepts writes but could not take this one - retry later.

A user variable can read its value from a file. Without **Allow write-back** it is read-only: every
operation answers `NotEditable` and the file is left alone. With write-back it behaves like any other
user variable, and Macro Deck also writes the new value to the file. This needs no new SDK: a plugin built
against an older one gets the same `NotEditable` it already handles for read-only variables. A user
variable that renders a template is read-only the same way: every operation answers `NotEditable`, and
its value changes only when a variable its template reads changes. `CreateAsync` always creates a
variable that holds its own value.

### Scopes

A user variable is global, or local to one widget, one script or one automation. A local variable
shadows a variable of the same name further out. A running flow resolves a name in this order:

| The flow runs in | Order |
| --- | --- |
| A widget | The widget's locals, then globals |
| A script | Its inputs, then the script's locals, then, for a script that runs on a widget, that widget's locals, then globals |
| An automation | The automation's locals, then globals |

A script started by **Run Script** resolves only along its own chain, never the caller's. Script inputs
are read-only: an operation on one is refused.

From an action, write in the context of the step that is running so script and automation locals are
found too. `UserVariableContext.From` copies the step's `OwnerWidgetId`, `ScriptId` and `AutomationId`
(see [What the executor gets](/features/actions/#what-the-executor-gets)). Here `_userVariables` is
the `IIntegrationContext.UserVariables` the integration kept from `InitializeAsync`:

```csharp
var variables = UserVariableContext.From(context);
var result = await _userVariables.ApplyInContextAsync(
	"count", variables, UserVariableOperation.Add, "1", context.CancellationToken);
if (result.Status == UserVariableWriteStatus.NotFound)
{
	await _userVariables.CreateInContextAsync(
		"count", variables, VariableType.Numeric, "1", cancellationToken: context.CancellationToken);
}
```

- `ApplyInContextAsync` resolves the name along the order above and otherwise behaves like `ApplyAsync`.
- `CreateInContextAsync` creates the variable in the most local scope the context names: the script,
  else the automation, else the owner widget, else global. It refuses a script that does not exist with
  `UnknownScript` and an automation that does not exist with `UnknownAutomation`.
- Both are default interface members. An `IUserVariableApi` implementation that predates them, and a host
  that predates script and automation variables, use only `OwnerWidgetId`: the call behaves like
  `ApplyAsync` or `CreateAsync` with the owner widget, so the plugin keeps working with widget and global
  variables. `ApplyAsync` and `CreateAsync` keep that behaviour on every host.
- Over the protocol, the context travels as the optional `scriptId` and `automationId` fields of the
  `user-variables` `apply` and `create` arguments, next to `ownerWidgetId`.
- In `MacroDeck.Plugin.Testing`, `ActionsTestClient.ExecuteWithContextAsync` runs a step inside a script or
  automation, and `FakeUserVariableApi` resolves script and automation locals and keeps the last context it
  was called with in `LastContext`.
