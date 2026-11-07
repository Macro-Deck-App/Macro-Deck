---
title: Deck and clients
description: Navigate folders and profiles with IDeckNavigator, fill pickers, and find out which folder each connected client has open.
---

`IIntegrationContext.Deck` is an `IDeckNavigator`. It moves clients between folders and profiles, lists
folders and profiles for pickers, and tells you where each connected client currently is.

## Navigate

```csharp
await context.Deck.ChangeFolderAsync(folderId);                 // every client
await context.Deck.ChangeFolderAsync(folderId, originClientId); // one client
await context.Deck.ChangeProfileAsync(profileId, originClientId);
await context.Deck.GoToParentAsync(originClientId);
await context.Deck.GoBackAsync(originClientId);
```

An action receives the pressing client's id in its execution context. Pass it as `originClientId` to
move only that client. A folder or profile that no longer exists is ignored.

`GetFolders()` and `GetProfiles()` return what a folder or profile picker should offer.

## Folders

`GetFolders()` lists every folder of every stored profile, sorted by label. Folders of a virtual profile
that an integration provides are not listed. Each `DeckFolder` also says where it sits in its profile's
folder tree, so you can walk the folders the way Macro Deck shows them:

| Member | Meaning |
| --- | --- |
| `Id` | The id to pass to `ChangeFolderAsync`. |
| `Label` | What a picker shows. With more than one profile it is prefixed with the profile name. |
| `ProfileId` | The profile the folder belongs to. |
| `ParentId` | The folder it is nested in, or `null` for a top-level folder. A parent is always in the same profile. |
| `Order` | Its place among the folders that share its parent: lower comes first. A sort key, not a contiguous index; equal values are ordered by `Id`. |

```csharp
// The folders of the profile a client has open, in the order the folder tree shows them.
IEnumerable<DeckFolder> Tree(IReadOnlyList<DeckFolder> folders, DeckClient client, string? parentId = null)
{
	return folders
		.Where(f => f.ProfileId == client.ProfileId && f.ParentId == parentId)
		.OrderBy(f => f.Order)
		.ThenBy(f => f.Id, StringComparer.Ordinal)
		.SelectMany(f => Tree(folders, client, f.Id).Prepend(f));
}
```

## Where each client is

There is no single "current folder": two devices can show different folders of the same profile.
`GetClients()` lists every connected client with the profile and folder it has open, and `ClientChanged`
fires when a client is first seen or moves.

```csharp
public sealed class ScopedHotkeysIntegration : IPluginIntegration
{
	private IDeckNavigator? _deck;

	public Task InitializeAsync(IIntegrationContext context)
	{
		_deck = context.Deck;
		_deck.ClientChanged += OnClientChanged;
		return Task.CompletedTask;
	}

	private void OnClientChanged(object? sender, DeckClientChangedEventArgs e)
	{
		// e.Client.ClientId moved from e.PreviousFolderId to e.Client.FolderId.
	}

	// A hotkey that should only move the client showing folder B.
	private Task NextFromFolderBAsync(string folderB, string folderC)
	{
		var client = _deck!.GetClients().FirstOrDefault(c => c.FolderId == folderB);
		return client is null ? Task.CompletedTask : _deck.ChangeFolderAsync(folderC, client.ClientId);
	}

	// Other IPluginIntegration members omitted; unsubscribe ClientChanged when the integration shuts down.
}
```

A `DeckClient` carries:

| Member | Meaning |
| --- | --- |
| `ClientId` | The id to pass as `originClientId` to navigate this client. |
| `DeviceId` | The paired device behind the client, or `null` for a client that is not a paired device. |
| `ProfileId`, `FolderId` | What the client has open. |
| `IsDeviceSession` | `true` for a device connected through a [device provider](/features/devices/). The provider draws the deck, so the client never shows Macro Deck's own UI, such as notification dialogs. `false` for a web or app client. |

`DeckClientChangedEventArgs` carries the new `Client` plus `PreviousProfileId` and `PreviousFolderId`, both
`null` when the client was not known yet.

### Rules

- `ClientChanged` fires for a new client and for a move. A client that re-reports the folder it already
  shows, for example after reconnecting, raises nothing.
- A leaving client raises nothing; it just disappears from `GetClients()`. A web or app client leaves
  once its connection has been gone for the same grace period that ends a Client Disconnected event. A
  device connected through a plugin leaves as soon as it goes offline, and comes back when it returns.
- A paired device is listed once. If it reconnects under a new client id, the new id replaces the old
  one.
- The web client keeps one id per browser origin, so two tabs of the same origin share one entry and the
  last one to move wins.
- Client ids of devices connected through a plugin start with `device:`.
- Handlers run synchronously while Macro Deck processes the move, so keep them short and hand longer work
  to a task.

### In a plugin

In a plugin, client positions arrive with the deck state the host pushes, not as one message per move.

- Handlers run on the plugin connection's receive loop. Keep them short, and hand longer work to a
  task. An exception thrown by a handler is logged and does not affect the connection.
- Quick moves can be coalesced: A to B to C may arrive as a single change from A to C. An integration
  running inside Macro Deck sees every step.
- When the plugin resumes its session, the first push reports the net change since the last state it saw.
  When it opens a new session, for example after the host restarted, `GetClients()` is empty until the
  first push, and that push reports every client as newly seen, with no previous profile or folder.

## Compatibility

`GetClients()` and `ClientChanged` are default interface members, so a type implementing
`IDeckNavigator` against an older SDK still compiles and loads. Against a host that predates client
positions, `GetClients()` returns an empty list and `ClientChanged` never fires. A host that predates
`IsDeviceSession` reports `false` for every client; its device sessions still have client ids starting with
`device:`. The wire format is
described in the [protocol reference](/reference/protocol/).

`ProfileId`, `ParentId` and `Order` on `DeckFolder` are `null` from a host that predates them. They are
optional properties, so code that creates a `DeckFolder` with only `Id` and `Label` still compiles.
