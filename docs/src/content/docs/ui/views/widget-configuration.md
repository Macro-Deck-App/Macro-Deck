---
title: Configuring a widget
description: Describing a widget's configuration as two named regions Macro Deck lays out, and reaching the editors the app already ships.
---

A widget's configuration is a `config` surface tree under the `widget-config` entry point, made of two
named regions Macro Deck lays out.

## Example

```csharp
public sealed class GaugeWidgetUiProvider : IUiProvider
{
    private const string GaugeWidgetType = "example.gauge";

    public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
    [
        new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
        new() { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
    ];

    public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
    {
        var attributes = request.Surface.Attributes;
        if (request.Surface.Kind != UiSurfaceKinds.Config ||
            !attributes.TryGetValue(UiConfigSurfaceAttributes.EntryPoint, out var entryPoint) ||
            entryPoint.GetString() != UiConfigEntryPoints.WidgetConfig ||
            !attributes.TryGetValue(UiConfigSurfaceAttributes.WidgetType, out var widgetType) ||
            widgetType.GetString() != GaugeWidgetType)
        {
            return Task.FromResult<IUiSession?>(null);
        }

        attributes.TryGetValue(UiConfigSurfaceAttributes.WidgetData, out var data);
        var root = GaugeConfigView.Build(data);

        return Task.FromResult<IUiSession?>(new ViewSession(new UiView(request.Surface, root)));
    }
}

public static class GaugeConfigView
{
    public static UiElement Build(JsonElement data)
    {
        var label = new UiState<string>(ReadString(data, "label") ?? string.Empty);
        var variable = new UiState<string>(ReadString(data, "variable") ?? string.Empty);
        var maximum = new UiState<double>(100);
        var border = new UiState<JsonElement>(data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty("border", out var b) ? b : default);
        var borderColor = new UiState<string>(string.Empty);
        var borderWidth = new UiState<double>(1);
        var flows = new UiState<JsonElement>(data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty("flows", out var f) ? f : default);

        return new UiWidgetConfiguration
        {
            Key = "root",
            Properties = new UiWidgetProperties
            {
                Key = "properties",
                Children =
                [
                    new UiStringInput { Key = "label", Label = Strings.Gauge.Label(), Binding = Bind.To(label) },
                    new UiVariablePickerInput
                    {
                        Key = "variable",
                        Label = Strings.Gauge.Variable(),
                        VariableTypes = UiValue.Of<IReadOnlyList<string>>(["Integer", "Float"]),
                        Binding = Bind.To(variable),
                    },
                    new UiNumberInput { Key = "maximum", Label = Strings.Gauge.Maximum(), Min = 1, Binding = Bind.To(maximum) },
                    new UiObjectInput
                    {
                        Key = "border",
                        Label = Strings.Gauge.Border(),
                        Binding = Bind.To(border),
                        Children =
                        [
                            new UiColorInput { Key = "color", Label = Strings.Gauge.BorderColor(), Binding = Bind.To(borderColor) },
                            new UiNumberInput { Key = "width", Label = Strings.Gauge.BorderWidth(), Min = 0, Max = 8, Binding = Bind.To(borderWidth) },
                        ],
                    },
                ],
            },
            Editor = new UiWidgetEditor
            {
                Key = "editor",
                Children = [new UiActionsListEditor { Key = "flows", Binding = Bind.To(flows), CanRun = true }],
            },
        };
    }

    private static string? ReadString(JsonElement data, string key)
        => data.ValueKind == JsonValueKind.Object && data.TryGetProperty(key, out var v) &&
           v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
```

`ViewSession` is the adapter from [Serving a view](/ui/views/sessions/#example). The built-in Clock widget
(`host/src/MacroDeckHost.Widgets/Clock/ClockWidgetConfigView.cs`) is a complete, larger example.

## The two regions

```csharp
new UiWidgetConfiguration
{
    Key = "root",
    Properties = new UiWidgetProperties { Key = "properties", Children = [/* fields */] },
    Editor = new UiWidgetEditor { Key = "editor", Children = [/* room for a big editor */] }, // optional
};
```

- `Properties` is the ordinary field list; the desktop editor draws it beside the preview.
- `Editor` is room a field list would not fit in, and is **optional**. Without it the editor is a single
  pane, not a split with an empty half.

You supply the fields. Macro Deck supplies the widget preview, the split layout and its narrow-window
drawer, the visual and JSON modes, scrolling, saving and the unsaved-changes prompt. Which side each region
lands on is the renderer's decision, and nothing in the contract names a renderer - a future native client
draws the same tree.

## An input's id is the data key it configures

```csharp
new UiStringInput { Key = "label" }                 // writes "label"
new UiObjectInput { Key = "border", Children =
    [new UiColorInput { Key = "color" }] }          // writes "border.color"
new UiArrayInput { Key = "states" /* ... */ }       // writes a JSON array
```

A region opens no input-id scope, so a top-level input's id is the widget data key it writes - in both
regions, which share one namespace. `UiObjectInput` and `UiArrayInput` do open a scope. Address array
items by their own stable key, never by position: a positional id loses focus and in-flight edits on
every reorder.

## The tree edits a draft it does not own

```csharp
attributes.TryGetValue(UiConfigSurfaceAttributes.WidgetData, out var data); // stored configuration
```

Nothing your tree does persists anything. Macro Deck accumulates the edits and writes them through the
ordinary widget save path on save, which keeps schema validation, JSON mode and the unsaved-changes prompt
working
([ADR 0050](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0050-ui-sessions-are-host-brokered.md)).
A plugin cannot read the host's stored widgets, so the configuration arrives on the surface. A key your
tree never mentions survives a save untouched, so a partial configuration drops nothing.

| `UiConfigSurfaceAttributes` | Value |
| --- | --- |
| `WidgetId` | The widget being configured. |
| `WidgetType` | Its type id. Check it before serving. |
| `WidgetData` | Its stored configuration. |
| `WidgetWidth`, `WidgetHeight` | Its size on the deck. That belongs to the deck: never part of `WidgetData`, never written back. |

## Reaching an editor Macro Deck already has

```csharp
new UiActionsListEditor { Key = "flows", Binding = Bind.To(flows), CanRun = true }
```

| Node type | DSL element | Configures |
| --- | --- | --- |
| `actions-list-editor` | `UiActionsListEditor` | The list of action flows, with triggers, ordering and nesting |
| `action-picker` | `UiActionPickerInput` | One action from the catalog of everything installed |
| `variable-picker` | `UiVariablePickerInput` | One variable, optionally narrowed to types (`VariableTypes`) or writable ones (`WritableOnly`). In the widget editor it lists global variables and the edited widget's own widget variables, never another widget's |
| `device-picker` | `UiDevicePickerInput` | One connected device |
| `integration-picker` | `UiIntegrationPickerInput` | One integration, or one of its configuration entries |
| `icon` | `UiIconReferenceInput` | One icon, as a typed provider reference |
| `icon-display` | `UiIconDisplayInput` | An icon's framing (fit, zoom, offset, opacity) over a preview of `Icon` at `AspectRatio` against `Background`; with `Tint` set, the preview draws the icon in that colour, keeping its transparency |

Each renderer maps these onto the editors it already ships; a plugin ships no renderer code for them.

`UiActionsListEditor.Triggers` names the trigger tabs the editor offers, such as `["onDoublePress"]` for a
single Double Tap tab. Leave it unset for the default press tabs: Short Press, Long Press, Touch Start, Touch
End and Double Tap. Event triggers, which run a flow when an integration or Macro Deck event fires, are
offered alongside those tabs either way. The host runs a widget's event flows only from the top-level
`flows` key of its stored configuration, so bind the editor there, as in the example above, for them to
fire.

- **`device-picker`** only populates where the viewer holds admin scope, because the device list is an
  administrative endpoint. The desktop editor does; prefer another picker where you have the choice.
- **A renderer without such an editor declines the type** like any unknown node type, and draws the node's
  `Fallback`. To stay configurable there, give the node a fallback built from primitives - a `json` input
  over the same value is usually enough.

### Letting the user adopt a provider action

A widget that follows an action's state or icon, like the Action Button, can let the user adopt one of the
actions in its own flows as that provider, right from the action list:

```csharp
new UiActionsListEditor
{
    Key = "flows",
    Binding = Bind.To(flows),
    OffersStateProvider = true,
    OffersIconProvider = true,
    StateProviderBlockId = UiValue.From(() => stateProvider.Value?.BlockId ?? string.Empty),
    Events = [UiEventHandler.OnAsync(UiConfigEvents.Provide, (data, _) => AdoptAsync(data))],
}
```

| Property | Meaning |
| --- | --- |
| `OffersStateProvider`, `OffersIconProvider` | The renderer marks actions that can provide states or an icon, and puts a control on each such action in the list. |
| `StateProviderBlockId`, `IconProviderBlockId` | The block the widget currently uses, so that control shows as active. Empty means none. |

Using a control raises `provide` on the node, with the payload
`{ "capability": "state" | "icon", "blockId": "...", "enabled": true | false }`. It is a request: your handler
decides whether to adopt, switch or stop, and the two block id properties report the result. The offer
properties do nothing unless the node also handles `provide`, so a renderer never shows a control that
cannot answer. A renderer or host that predates these properties and the event ignores them, and the action
list looks the way it always did. To show which action currently provides, the Action Button uses a
[status line](/ui/views/configuration/#showing-what-governs-a-setting).

## Declining

```csharp
if (widgetType.GetString() != GaugeWidgetType)
{
    return Task.FromResult<IUiSession?>(null);
}
```

Returning `null` declines and is not an error. But unlike an action or a config flow, a widget has **no
declared field list to fall back to**: a declined widget configuration leaves the user with JSON mode only.
Decline only widgets you genuinely do not configure, and check `WidgetType` rather than assuming the
surface is yours.

## See also

- [Serving a configuration view](/ui/views/configuration/)
- [Deck widget views](/ui/views/widget/)
- [Widget types](/ui/views/widget-types/)
- [Serving a view](/ui/views/sessions/)
