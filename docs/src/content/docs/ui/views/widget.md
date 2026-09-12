---
title: Deck widget views
description: The widget surface - what it carries, and how a provider is handed one.
---

A deck widget is a tile on the deck drawn from your tree, served over a `widget` surface.

## Example

```csharp
public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
[
    new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
];

public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
{
    if (request.Surface.Kind != UiSurfaceKinds.Widget)
    {
        return Task.FromResult<IUiSession?>(null);
    }

    var data = request.Surface.Attributes[UiWidgetSurfaceAttributes.Data];
    var city = data.TryGetProperty("city", out var value) ? value.GetString() : null;

    var view = new UiView(request.Surface, Weather(city ?? "Berlin"));
    return Task.FromResult<IUiSession?>(new ViewSession(view));
}

private UiStack Weather(string city) => new()
{
    Key = "weather",
    Justify = UiComponentJustify.SpaceBetween,
    Children =
    [
        new UiStack
        {
            Key = "current",
            Direction = UiComponentDirections.Horizontal,
            Justify = UiComponentJustify.SpaceBetween,
            Children =
            [
                new UiStack
                {
                    Key = "labels",
                    Children =
                    [
                        new UiTextRun { Key = "location", Text = city, Size = 0.09 },
                        new UiTextRun { Key = "temp", Text = UiText.From(() => $"{_forecast.Value.Temperature}°"), Size = 0.26 },
                    ],
                },
                new UiImage { Key = "icon", Source = _sun, Size = 0.26 },
            ],
        },
        new UiTextRun
        {
            Key = "condition",
            Text = UiText.From(() => _forecast.Value.Condition),
            Size = 0.08,
            Role = UiComponentTextRoles.Secondary,
        },
        new UiRangeBar { Key = "range", MainSize = 0.06, Start = 0.2, End = 0.6, Marker = 0.45 },
    ],
};
```

![A two-by-two weather widget: Berlin, 21°, a sun icon, the caption Sunny and a temperature range bar](../../../../assets/ui/view-widget.png)

To offer a widget in the picker, register a [widget type](/ui/views/widget-types/) - that page covers
registration, the picker card and configuration. The same vocabulary also draws a
[folder view](/ui/views/folder-views/) and an [action modal](/ui/views/modal/).

## Forwarding to a UiView

`ViewSession`, the adapter between a `UiView` and `IUiSession` used on every view page, is defined on
[Serving a view](/ui/views/sessions/#example).

## What the surface carries

| Attribute (`UiWidgetSurfaceAttributes`) | Meaning |
| --- | --- |
| `widgetId` | The widget being drawn. |
| `widgetType` | Its qualified widget type id. |
| `data` | Its stored configuration, as JSON. |
| `cornerRadius` | The tile's corner radius. |
| `sample` | `true` when the picker asks for a representative sample. |
| `ghost` | `true` when drawing the drag ghost of a widget that is also drawn live. |

The stored configuration travels with the request rather than being looked up, so a provider outside
the host can serve a widget. A provider that ignores `sample` or `ghost` keeps behaving exactly as it
did.

## Previews, samples and ghosts

```csharp
var attributes = request.Surface.Attributes;
var isSample = attributes.TryGetValue(UiWidgetSurfaceAttributes.Sample, out var sample)
    && sample.ValueKind == JsonValueKind.True;
var isGhost = attributes.TryGetValue(UiWidgetSurfaceAttributes.Ghost, out var ghost)
    && ghost.ValueKind == JsonValueKind.True;
```

An editor preview arrives as a `preview` surface carrying draft configuration instead. A `preview` with
`sample: true` is a picker card: draw it without reading anything live - nothing is connected, bound or
configured while someone is choosing a widget type. `ghost` exists because two surfaces for one widget are
otherwise identical, and without it the ghost and the tile would share a session.

## Artwork

`_sun` above is a [resource handle](/ui/reference/resources/). Registering your own artwork needs an
upload path that plugins do not have yet; that is an additive step the component vocabulary was shaped
for.

## See also

- [Widget types](/ui/views/widget-types/)
- [Serving a view](/ui/views/sessions/)
- [Components](/ui/components/)
