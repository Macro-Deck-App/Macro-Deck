---
title: Widget types
description: Offering your own deck widget - registering the type, drawing it, and configuring it.
---

A *widget type* is a kind of tile a user can add to a deck. Macro Deck ships six - the action button, the
clock, the slider, the weather, the music player and the history graph - and a widget type provider offers
more. A widget of your type is placed, moved, resized, exported and imported exactly like a built-in one,
and it is drawn by the same UI runtime: there is no separate rendering path for a plugin's widget.

## The contract

Implement `IWidgetTypeProvider` to say which types you offer, and
[`IUiProvider`](/sdk/ui/views/sessions/) to draw them:

```csharp
public sealed class GaugeIntegration : IIntegration, IWidgetTypeProvider, IUiProvider
{
	public string ProviderName => "Gauges";

	public async Task InitializeAsync(
		IWidgetTypeProviderContext context,
		CancellationToken cancellationToken = default)
	{
		await context.RegisterWidgetTypeAsync(
			new WidgetTypeDescriptor(
				"gauge",
				MyStrings.GaugeName(),
				MyStrings.GaugeDescription(),
				DefaultData: """{"unit":"C"}""",
				DataSchema: MyStrings.GaugeSchema,
				HasConfiguration: true),
			cancellationToken);
	}
}
```

`RegisterWidgetTypeAsync` returns the qualified id - `your.plugin.id::gauge` - that a widget stores as its
type. **That id has to stay stable across releases:** every widget already on a deck names it, and renaming
it strands all of them. It is also why an id must never be reused for a different widget.

Registration is a push, not a getter Macro Deck calls: register whenever you are ready, and register again
under the same local id to change a type's name, default data, schema or configuration flag. Widgets already
placed pick the new descriptor up without being touched. `GetWidgetTypes()` exists only so Macro Deck can
recover its catalog after a reconnect, and answering it is optional.

There is deliberately no `ShutdownAsync` on this interface. Release whatever `InitializeAsync` acquired in
your integration's own `ShutdownAsync`; Macro Deck withdraws your registered types itself.

### What the descriptor carries

`DefaultData` is the stored configuration a newly added widget starts with, as a JSON object. Absent reads
as `{}`. If your widget cannot draw anything sensible from an empty object, say what it starts as here
rather than defending against `{}` in every session.

`DataSchema` is a JSON Schema for the widget's stored data. Macro Deck validates every save against it, so a
configuration tree cannot write a shape you will not be able to read back. It is optional for a type whose
data is fixed - and **required when `HasConfiguration` is true**, because a configuration surface writing
into an unvalidated payload is exactly how a widget's data becomes unreadable with nothing reporting an
error.

There is no icon. A widget picker card draws a live sample of the widget itself (see below), so an icon
would have no reader.

## Drawing the widget

Macro Deck opens a `widget` surface against your `IUiProvider` for each tile of your type on screen:

```csharp
public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
[
	new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
	new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
	new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
];

public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
{
	var attributes = request.Surface.Attributes;

	if (request.Surface.Kind is UiSurfaceKinds.Widget or UiSurfaceKinds.Preview)
	{
		// Decline a type you do not serve rather than guessing from the data's shape.
		if (attributes[UiWidgetSurfaceAttributes.WidgetType].GetString() != "your.plugin.id::gauge")
		{
			return Task.FromResult<IUiSession?>(null);
		}

		return Task.FromResult<IUiSession?>(
			new GaugeSession(attributes[UiWidgetSurfaceAttributes.Data]));
	}

	return Task.FromResult<IUiSession?>(null);
}
```

The surface carries the keys [the widget surface](/sdk/ui/views/widget/) documents - `widgetId`,
`widgetType`, `data`, `cornerRadius`, and `ghost` when the tile being drawn is a drag ghost of one that is
also on screen. The widget's stored data travels with the request rather than being looked up, because you
cannot read Macro Deck's stored widgets. That also means you never have to distinguish a saved widget from a
draft: both arrive the same way.

One session is opened per widget per viewer, so two tiles of your type each get their own and each sees only
its own data. Push patches on a session to update a widget after it has been created; the events your tree
declares are dispatched back to that same session.

### The picker card

A widget picker card is a live `preview` surface with `sample: true` - a representative sample, drawn
without reading anything live, because nothing is connected or configured at the moment somebody is choosing
a widget type. Serving it is optional: a type whose provider declines gets a card naming the type instead of
a drawn one, and stays pickable either way.

## Configuration

Set `HasConfiguration` and Macro Deck opens a `config` surface with the `UiConfigEntryPoints.WidgetConfig`
entry point when the user edits a widget of your type. Build it with the
[widget configuration view](/sdk/ui/views/widget-configuration/), whose two regions Macro Deck lays out
around your tree. The values the user enters are stored with the widget and handed back to you on every
later `widget` surface.

You need no second contract for this: your widget already is an `IUiProvider`, so it declares one more
surface and branches on the entry point.

## When your integration is not running

A widget keeps its type and its data whether or not anything provides them. It is stored, exported and
re-imported unchanged; Macro Deck simply has nothing to draw it with until your type comes back, and then
every widget of it returns exactly as it was. The same is true of an archive imported on a machine where
your plugin is not installed yet.

Withdrawing a type with `UnregisterWidgetTypeAsync` therefore never destroys anyone's deck. It stops the
type being *offered* in the picker; widgets already using it wait for it to come back. Macro Deck keeps your
type's catalog entry across a mere disconnect, so a restarting plugin does not leave every widget of its type
nameless - the entry goes only when your integration is uninstalled or stopped.

A press on a widget of your type runs nothing on the host. Its interactions are the events its own tree
declares, dispatched over its session; a tile whose tree declares none simply does nothing when pressed.

## Related documentation

- [Macro Deck UI](/sdk/ui/) - the component model a widget is built from.
- [The widget surface](/sdk/ui/views/widget/) - the attribute keys in full.
- [Configuring a widget](/sdk/ui/views/widget-configuration/) - the two-region configuration tree.
- [Capabilities](/sdk/capabilities/) - how `widget-type-provider` is declared.
- [Folder views](/sdk/folder-views/) - the same registration shape, one level up.
