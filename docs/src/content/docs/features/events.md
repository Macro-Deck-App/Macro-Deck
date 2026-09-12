---
title: Events
description: Declare events with IEventProvider, publish occurrences with IEventPublisher, and let users filter them with configuration parameters and dynamic options.
---

An integration tells Macro Deck "this just happened" with events. `IEventProvider` declares which events
exist; `IEventPublisher`, handed to you on the integration context, publishes an occurrence. Users react
to them with triggers.

## Quick start

```csharp
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;

public sealed class StreamStudioIntegration : IPluginIntegration, IEventProvider
{
	private StudioClient? _client;

	public IReadOnlyList<EventDefinition> EventDefinitions { get; } =
	[
		new()
		{
			Id = "scene-changed",
			Name = Strings.Events.SceneChanged(),
			Category = Strings.Events.ScenesCategory(),
			ConfigurationParameters =
			[
				ActionParameter.DynamicChoice("sceneId",
					label: Strings.Parameters.Scene(),
					placeholder: Strings.Parameters.AnyScene())
			],
			PayloadParameters =
			[
				ActionParameter.DynamicChoice("sceneId", label: Strings.Parameters.SceneId()),
				ActionParameter.Text("sceneName", label: Strings.Parameters.Scene())
			]
		}
	];

	public Task InitializeAsync(IIntegrationContext context)
	{
		var events = context.Events; // safe to keep for the process lifetime
		_client = new StudioClient();
		_client.SceneChanged += scene => events.Publish("scene-changed", new Dictionary<string, object?>
		{
			["sceneId"] = scene.Id,
			["sceneName"] = scene.Name
		});
		return Task.CompletedTask;
	}

	// Remaining IPluginIntegration members omitted.
}
```

The user now finds "Scene changed" in the trigger editor, can narrow it to one scene, and can use
`sceneId` and `sceneName` in the flow it runs.

Things to know:

- **`Id` is persisted** in every trigger that uses the event. Treat it as a public identity: never
  rename it once shipped. It must be unique within your integration; the host namespaces it as
  `integrationId::eventId`.
- **`Publish` is fire-and-forget.** It never throws into the caller and has no reply, so it is safe to
  call from a socket callback or polling loop. An occurrence nobody subscribed to is simply dropped.
- **Payload keys must match the declaration.** Publish the names you listed in `PayloadParameters` -
  those are what the user can pick and filter on.

## Defining an event

| Property | What it does | Example |
| --- | --- | --- |
| `Id` | Provider-local id, stable across releases. | `"scene-changed"` |
| `Name`, `Description` | Localized text in the event picker. | `Strings.Events.SceneChanged()` |
| `Category` | Groups events in the picker. Optional. | `Strings.Events.ScenesCategory()` |
| `IconName` | An icon the UI already ships. No image data. | `"movie"` |
| `ConfigurationParameters` | What the user authors on the trigger - see below. | `ActionParameter.DynamicChoice("sceneId", ...)` |
| `PayloadParameters` | What an occurrence carries. Never rendered as an input. | `ActionParameter.Text("sceneName", ...)` |
| `DeliveryKind` | `Push` (default): you publish. `Scheduled`: the host produces occurrences from the configuration. | `EventDeliveryKind.Push` |

`ProviderName` on `IEventProvider` is optional; leave it empty and the event picker shows your
integration's name (for a plugin, the `manifest.json` name).

`EventDefinitions` is read whenever the host builds the event catalogue, so it may change after a
reconfigure. An out-of-process plugin must tell the host to re-read it:

```csharp
using MacroDeck.Plugin.Hosting.Integrations.HostApis; // IPluginCatalogNotifier, injected
using MacroDeck.Plugin.Protocol.Handshake;             // CapabilityKinds

catalogNotifier.CatalogChanged(CapabilityKinds.Events);
```

## Configuration parameters vs payload parameters

```csharp
ConfigurationParameters =
[
	ActionParameter.DynamicChoice("trackId", label: Strings.Parameters.Track(), placeholder: Strings.Parameters.AnyTrack())
],
PayloadParameters =
[
	ActionParameter.DynamicChoice("trackId", label: Strings.Parameters.TrackId()),
	ActionParameter.Text("trackName", label: Strings.Parameters.Track()),
	ActionParameter.Toggle("muted", label: Strings.Parameters.Muted())
]
```

Both lists use the ordinary `ActionParameter` schema, so any control the action builder renders is
available.

- **Configuration parameters** are what the user fills in on the trigger. When one shares its name with
  a payload parameter, the host compares the two and only fires the trigger on a match. Left empty, it
  matches any occurrence - "any track" above.
- **Payload parameters** describe what an occurrence carries. They populate the picker that inserts
  `{ "$event": "trackName" }` references into the triggered flow and label values in the live preview.
  When a user writes a condition against `$event`, the comparison value is authored with the control the
  payload parameter's type implies, while the condition still stores the raw value the occurrence
  carries. So declare an enumerable payload value the same way as its matching filter (a `DynamicChoice`
  here): the user picks a track name instead of pasting an id.

## Publishing values

```csharp
_events.Publish("hotkey-pressed", new Dictionary<string, object?>
{
	["key"] = "F3",                                                  // string
	["repeat"] = 2,                                                  // number
	["held"] = false,                                                // boolean
	["combo"] = new { modifiers = new[] { "Ctrl", "Shift" }, key = "F3" } // object
});
```

| Published value | What triggers, templates and conditions see |
| --- | --- |
| string, number, boolean | the value itself |
| object or array | its compact JSON text: `{"modifiers":["Ctrl","Shift"],"key":"F3"}` |

A condition on an object or array value compares that text. Publishing without parameters is fine for
events that carry nothing:

```csharp
_events.Publish("connected");
```

## Dynamic options

```csharp
public sealed class StreamStudioIntegration : IPluginIntegration, IEventProvider, IDynamicEventOptionsProvider
{
	public Task<DynamicOptionsResult> GetEventOptionsAsync(EventOptionsContext context,
		CancellationToken cancellationToken)
	{
		var session = _client?.Session ?? StudioSession.Empty;

		IReadOnlyList<ActionParameterOption> options = context.ParameterName switch
		{
			"sceneId" => session.Scenes.Select(s => new ActionParameterOption { Value = s.Id, Label = s.Name }).ToList(),
			_ => []
		};

		return Task.FromResult(new DynamicOptionsResult
		{
			Options = options,
			AllowsCustomValue = true,
			CacheSeconds = 30
		});
	}
}
```

Implement `IDynamicEventOptionsProvider` when a choice is only known at edit time - a scene list, a
device, a channel. It answers configuration and payload parameters alike and never changes the event
definition itself. `EventOptionsContext` carries:

| Member | Meaning |
| --- | --- |
| `EventId` | Provider-local event id, without the `integrationId::` prefix. |
| `ParameterName` | The parameter being edited. |
| `Filter` | Text the user typed, for a filterable autocomplete. |
| `CurrentParameters` | The other configuration values, so options can depend on an earlier choice. |

The context names only the event and the parameter, so a request for the configuration parameter
`sceneId` looks exactly like one for the payload parameter `sceneId`. Declare the same name in both lists
only where the same options answer for both - as in every example on this page.

## Testing

```csharp
var context = new FakeIntegrationContext();
await integration.InitializeAsync(context);

studio.RaiseSceneChanged(new Scene("s1", "Intro"));

var published = context.Events.Published.Single();
Assert.That(published.EventId, Is.EqualTo("scene-changed"));
Assert.That(published.Parameters!.Value.GetProperty("sceneName").GetString(), Is.EqualTo("Intro"));
```

`FakeEventPublisher` serializes parameters exactly as the wire protocol does, and like the real one it
never throws. `PluginTestHarness.Events` calls `describe` and `options` over the protocol. See
[testing](/features/testing/).

## Over the plugin protocol

Events are one provider-shaped capability per plugin: `describe` returns the merged catalogue of every
`IEventProvider` in the process, `options` routes to `IDynamicEventOptionsProvider`. The catalogue is
snapshot-backed. Occurrences travel as the fire-and-forget
[`event.publish`](/reference/websocket/#events-logs-and-state) message; the host qualifies the id with
the authenticated plugin id. See [capability parity](/reference/capability-parity/).

## See also

- [Actions](/features/actions/) - the `ActionParameter` schema both parameter lists use.
- [Variables](/features/variables/) - for state that is read rather than announced.
- [Localization](/features/localization/#the-generated-api) - where `Strings.*` comes from.
- [WebSocket reference](/reference/websocket/#capabilities)
