---
title: Deck widget views
description: The widget surface - what it carries, and how a provider is handed one.
---

A deck widget is one place the framework's component vocabulary renders - the vocabulary itself belongs
to the framework and is covered in full under [Components](/ui/components/); this page is where
widget-surface-specific material lives.

A plugin serves a widget surface by registering a widget type and declaring the surface on its
`IUiProvider` - see [Widget types](/ui/views/widget-types/), which covers registration, the picker card and
configuration. Serving your own artwork still needs an upload path that does not exist yet; that remains an
additive step the component vocabulary was shaped for.

The same vocabulary draws a [folder view](/ui/views/folder-views/) and an action modal, both of which a plugin
can serve too.

## The widget surface

A widget surface carries the widget's stored configuration as surface attributes rather than expecting
the provider to look it up, precisely so that a provider outside the host can serve one.

```csharp
public IReadOnlyList<UiSurfaceDeclaration> Surfaces =>
    [new UiSurfaceDeclaration { Kind = "widget", SessionMode = "shared" }];
```

![A two-by-two weather widget: Berlin, 21°, a sun icon, the caption Sunny and a temperature range bar](../../../../assets/ui/view-widget.png)

`UiWidgetSurfaceAttributes` names the keys: `widgetId`, `widgetType`, `data`, `sample` and `ghost`.

An editor preview arrives as a `preview` surface carrying draft configuration instead. A `preview` surface
whose `sample` attribute is `true` asks for a representative sample of the widget - what the widget picker
draws in each card - so it must be drawn without reading anything live: nothing is connected, bound or
configured at the moment somebody is choosing a widget type.

`ghost` is present and `true` when the surface draws the drag ghost of a widget that is also being drawn
live. Two surfaces for one widget are otherwise identical, so without it the ghost and the tile would share
a session.

A provider that ignores either key keeps behaving exactly as it did.
