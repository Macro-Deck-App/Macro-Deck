---
title: Folder views
description: Replacing a folder's whole surface with a Macro Deck UI view, and what Macro Deck keeps for itself.
---

A *folder view* renders the whole of one folder. Macro Deck's built-in view is the widget grid every deck
has always had; a folder view provider offers alternatives - a Home Assistant dashboard, an OBS mixer, a
monitoring panel - and a folder picks one the same way it picks a name.

A folder view is not a different kind of folder. A folder stays an ordinary folder and its view is part of
its configuration, so it can be changed later and nothing else about the folder changes with it.

## The contract

Implement `IFolderViewProvider` to say which views you offer, and
[`IUiProvider`](/ui/views/sessions/) to serve them:

```csharp
public sealed class HomeAssistantIntegration : IIntegration, IFolderViewProvider, IUiProvider
{
	public string ProviderName => "Home Assistant";

	public async Task InitializeAsync(
		IFolderViewProviderContext context,
		CancellationToken cancellationToken = default)
	{
		await context.RegisterFolderViewAsync(
			new FolderViewDescriptor(
				"dashboard",
				MyStrings.DashboardName(),
				MyStrings.DashboardDescription(),
				HasConfiguration: true),
			cancellationToken);
	}
}
```

`RegisterFolderViewAsync` returns the qualified id - `your.plugin.id::dashboard` - that a folder stores.
**That id has to stay stable across releases:** folders reference it, and renaming it strands every folder
already using the view.

Registration is a push, not a getter Macro Deck calls: your provider registers whenever it is ready and
withdraws whenever it is not. `GetFolderViews()` exists only so Macro Deck can recover its catalog after a
reconnect, and answering it is optional.

There is deliberately no `ShutdownAsync` on this interface. Release whatever `InitializeAsync` acquired in
your integration's own `ShutdownAsync`; Macro Deck withdraws your registered views itself.

## Serving the view

Macro Deck opens a `folder` surface against your `IUiProvider` when a client shows a folder that selected
one of your views:

```csharp
public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
[
	new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Folder, SessionMode = UiSessionModes.Shared },
];

public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
{
	if (request.Surface.Kind != UiSurfaceKinds.Folder)
	{
		return Task.FromResult<IUiSession?>(null);
	}

	var attributes = request.Surface.Attributes;
	var viewId = attributes[UiFolderSurfaceAttributes.ViewId].GetString();
	var configuration = attributes[UiFolderSurfaceAttributes.Configuration];

	// Decline a view you do not serve rather than guessing from the configuration's shape.
	return Task.FromResult<IUiSession?>(viewId == "your.plugin.id::dashboard"
		? new DashboardSession(configuration)
		: null);
}
```

The surface carries `folderId`, `folderName`, `viewId` and `configuration`. The configuration travels with
the request rather than being looked up, for the same reason a widget's data does: you cannot read Macro
Deck's stored folders.

A folder view is built from [Macro Deck UI components](/ui/components/) - the same `ui.stack`,
`ui.text`, `ui.button` and friends a deck widget uses. Two consequences worth planning for:

- **A folder view does not scroll.** Everything you draw has to fit the box you are given.
- **Lengths are fractions of the box's *smaller* side**, as they are in a deck widget. A folder view is
  usually much wider than it is tall, so your sizes track its height. Size against that rather than
  against the width you can see.

## Configuration

Set `HasConfiguration` and Macro Deck opens a `config` surface with the
`UiConfigEntryPoints.FolderViewConfig` entry point when the user picks your view - inside the folder's own
dialog, not as a flow of its own. Build it with the
[configuration view](/ui/views/configuration/); the values the user enters are stored
with the folder and handed back to you on every later `folder` surface.

The surface carries `folderId`, `folderViewId` and `folderViewConfiguration`. `folderViewId` is the view
being configured, which is not always the one the folder currently stores - the user is choosing.

## Navigation is Macro Deck's

Macro Deck draws the back button, and it always drives Macro Deck's own navigation stack. You cannot
redirect it, and you do not need to draw one.

`FolderViewNavigation.Hidden` says you offer a self-contained way out and would rather Macro Deck drew
nothing. It is a preference, not a guarantee: Macro Deck shows its button anyway when the view is the only
thing on screen and there is somewhere to go back to. That is deliberate - an incomplete or broken view
must never be able to trap someone inside it. Equally, Macro Deck draws no button at a root folder, where
there is nowhere to go, whatever you asked for.

## When your integration is not running

A folder keeps its view id and its configuration whether or not anything provides them. Macro Deck renders
a placeholder naming the missing view, offering the integrations page and the folder's own settings; the
stored id and configuration are never cleared, so enabling your integration again brings the folder back
exactly as it was. The same is true of an archive imported on a machine where your plugin is not installed
yet.

Withdrawing a view with `UnregisterFolderViewAsync` therefore never destroys anyone's folder. It stops the
view being *offered*; folders already using it wait for it to come back.

## Where a folder view can be chosen

Macro Deck offers the choice - in a folder's context menu, and when creating one - only where it leads
somewhere: when more than one view is on offer, and when the device claiming the profile can render one.
The second half is a layout capability, `LayoutVisualCapabilities.CustomFolderViews`; see
[Layout providers](/features/layouts/). A profile no device claims keeps the choice.

That means your view can be registered and still not appear as an option on a particular profile. It is
not a failure to handle: nothing selects it, so nothing opens a session for it.

## Over the plugin protocol

Registration is driven from the plugin side, so the host-to-provider direction of the
`folder-view-provider` capability only has to describe the provider and re-read its catalog after a
reconnect:

| Operation | Purpose |
| --- | --- |
| `describe` | The provider's declared name and its current catalog. |
| `folder-views` | The provider's current folder view catalog, re-read after a reconnect. |

Registering and withdrawing a view travels the other way as the `folder-views` host API:

| Operation | Purpose |
| --- | --- |
| `register` | Registers a folder view, or replaces one already registered under the same provider-local id. |
| `unregister` | Withdraws a folder view. Folders still using it keep their stored id and configuration and show a placeholder until it returns. |

The views themselves are served over the `ui` capability, like every other Macro Deck UI surface.

## Related documentation

- [Macro Deck UI](/ui/) - the component model a folder view is built from.
- [Layout providers](/features/layouts/) - the same registration shape, one level down.
