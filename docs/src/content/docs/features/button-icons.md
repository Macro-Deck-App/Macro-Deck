---
title: Button icons
description: Let an action own an action button's rendered icon with IIconProviderActionDefinition - album art, avatars, weather imagery - by version, reference or bytes.
---

An icon-provider action supplies the image an action button currently shows, such as album artwork, an
avatar or a weather symbol. It overrides the button's configured icon while it answers, and hands it
back when it doesn't.

## Quick start

```csharp
using MacroDeck.Sdk.Actions;

internal sealed class NowPlayingAction(PlayerClient player) : IActionDefinition, IIconProviderActionDefinition
{
	public string Id => "now-playing";

	public Task<ActionIconSnapshot?> GetActionIconAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		var artwork = player.CurrentArtwork; // already held - never fetch here
		ActionIconSnapshot? snapshot = !player.IsConnected ? null
			: artwork is null ? new ActionIconSnapshot { NoIcon = true }
			: new ActionIconSnapshot { Version = artwork.TrackId, MediaType = artwork.MediaType };

		return Task.FromResult(snapshot);
	}

	public Task<ActionIconContent?> GetActionIconContentAsync(
		IReadOnlyDictionary<string, object?> parameters,
		string version,
		CancellationToken cancellationToken)
	{
		var artwork = player.CurrentArtwork;
		return Task.FromResult(artwork?.TrackId == version
			? new ActionIconContent(artwork.Bytes, artwork.MediaType)
			: null);
	}

	// Name, Description, Parameters and CreateExecutor omitted - see Actions.
}
```

A button running **Now playing** shows the current track's cover. Bytes are fetched once per track, and
the button's own icon comes back when the player is disconnected.

Things to know:

- **Three answers.** `null` means "I can't answer", and the button's configured icon is shown. `NoIcon`
  means "deliberately blank". A `Version` means "this image".
- **Bytes follow `Version`.** The host calls `GetActionIconContentAsync` only when `Version` changes, which
  keeps polling cheap.
- **Render-time only.** The button's configured icon is never rewritten, and it reappears the moment the
  provider is removed or disabled.
- **Independent of states.** It is free-standing (it does not extend `IActionDefinition`) and separate
  from [`IStateProviderActionDefinition`](/features/button-states/). Enabling one never enables the
  other.

## Choosing an answer

```csharp
// Can't answer right now - the widget shows its configured icon.
return Task.FromResult<ActionIconSnapshot?>(null);

// Working, and deliberately showing nothing.
return Task.FromResult<ActionIconSnapshot?>(new ActionIconSnapshot { NoIcon = true });

// An icon the host already has - no bytes needed.
return Task.FromResult<ActionIconSnapshot?>(new ActionIconSnapshot
{
	Version = condition,
	Reference = ActionIconReference.IconPack(WeatherIcons.For(condition))
});

// Your own bytes - fetched through GetActionIconContentAsync when Version changes.
return Task.FromResult<ActionIconSnapshot?>(new ActionIconSnapshot { Version = avatarHash, MediaType = "image/png" });
```

| `ActionIconSnapshot` | |
| --- | --- |
| `Version` | Stable identity of the image. The host refetches bytes only when it changes. Empty with `NoIcon`. |
| `Reference` | A host-resolvable icon, e.g. `ActionIconReference.IconPack(id)`. Opaque, and **never a URL** - the host never fetches one on a provider's say-so. |
| `MediaType` | Media type of the bytes, when there is no `Reference`. |
| `NoIcon` | Render nothing. |

`GetActionIconContentAsync` returns the bytes for the `version` asked for. Return `null` when that version
has already moved on, and the host keeps the image it already holds rather than blanking the button. It
is never called for a snapshot with a `Reference` or `NoIcon`. The default implementation returns `null`,
so a reference-only provider does not override it.

## Reading the icon

The rules match [reading state](/features/button-states/#reading-state):

- **Answer for the configured instance**, from `parameters`. The same action on several buttons answers
  for each separately. The host decides which instance, if any, is a button's icon provider (at most one
  per button), and resolves it once per button - you own the icon currently rendered, not one icon per
  state.
- **Tolerate a half-filled draft.** It is called once per settled editor draft while the user configures
  the action, so a missing value must not throw.
- **Side-effect free.** Answer from state you already hold, honour the token, never connect or
  authenticate, and never route through `IActionExecutor`.

`IconPollInterval` (default five seconds) is a request, clamped like `StatePollInterval`, and the host
reads less often while nothing displays the button.

## Pushing a change

```csharp
public async Task InitializeAsync(IIntegrationContext context)
{
	_player.TrackChanged += async (_, _) => await context.Widgets.InvalidateIconAsync("now-playing");
	// ...
}
```

`InvalidateIconAsync` makes every button following that action re-read its icon now, instead of at the
next poll. Pass the action's **declared local id**, not a configured instance: the host qualifies it with
your integration, so you can only invalidate your own actions. It is a hint - the host still compares
`Version` before refetching. It does not replace polling, and against a host that predates it the call
does nothing.

## When the provider can't answer

A `null` snapshot, a timeout, an exception, or a missing or disabled block or integration all fall back to
the button's own configured icon. This is the opposite of a state provider, which holds its last state. An
icon has a meaningful default to return to, so a button never shows a stale image.

The **Set Icon** action fails on a button with an assigned icon provider, the same way it fails on a
state-controlled one. This holds even when the provider can't answer right now. In a patch that changes
several properties, only the icon is dropped and the rest still applies.

## Over the plugin protocol

| Operation | SDK member |
| --- | --- |
| `icon` | `GetActionIconAsync` |
| `icon.content` | `GetActionIconContentAsync` |

Test both with `PluginTestHarness.Actions.GetActionIconAsync(localId, parameters)` and
`GetActionIconContentAsync(localId, version, parameters)`.

## See also

- [Actions](/features/actions/) and [Button states](/features/button-states/)
- [Capability parity: the icon-poll window](/reference/capability-parity/#the-icon-poll-window)
- [Devices: fetching a provider-controlled icon](/features/devices/#fetching-a-provider-controlled-icon)
