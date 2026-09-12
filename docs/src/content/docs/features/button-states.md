---
title: Button states
description: Drive an action button's N-state appearance with IStateProviderActionDefinition - declaring states, reporting the active one, default appearances and expected states.
---

A state-provider action tells a button which states it can be in and which one is current - muted or
unmuted, playing or paused. The button follows along and shows the appearance the user gave each state.

## Quick start

```csharp
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

internal sealed class MicMuteAction(VoiceClient client) : IActionDefinition, IStateProviderActionDefinition
{
	private static readonly IReadOnlyList<ActionStateDefinition> _states =
	[
		new("unmuted", MacroDeckStrings.States.Unmuted())
		{
			DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#2f855a" }
		},
		new("muted", MacroDeckStrings.States.Muted())
		{
			DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#c53030" }
		},
		new("unavailable", MacroDeckStrings.States.Unavailable())
		{
			DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#4a5568", LabelColor = "#cbd5e0" }
		}
	];

	public string Id => "toggle-mute";

	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		// Answer from state the client already holds - never connect here.
		var active = client.IsConnected ? client.IsMuted ? "muted" : "unmuted" : "unavailable";
		return Task.FromResult<ActionStateSnapshot?>(new ActionStateSnapshot(_states, active));
	}

	// Name, Description, Parameters and CreateExecutor omitted - see Actions.
}
```

A button running **Toggle mute** can adopt it as its state provider. It starts out green or red with the
labels "Unmuted"/"Muted", and switches whenever the mic does.

Things to know:

- **Answer for the configured instance.** `parameters` is the instance's configuration. The same
  action can sit on several buttons with different parameters, and each answers for itself.
- **The host picks the provider.** It decides which instance, if any, drives a button (at most one per
  button). The action never claims a button for itself.
- **It is free-standing.** `IStateProviderActionDefinition` does not extend `IActionDefinition`. Implement
  both on the same class.
- **Read-only and cheap.** Called while the user is still editing and polled while the button is on
  screen, so see [the rules below](#reading-state).

## Declaring states

```csharp
new ActionStateDefinition("recording", MacroDeckStrings.States.Recording())
{
	DefaultAppearance = new ActionStateAppearance
	{
		Label = "",                   // icon-only
		BackgroundColor = "#c53030",
		LabelColor = "#ffffff"
	}
}
```

A state `Id` is persisted in the user's profile once a button adopts it. Keep it stable lowercase
kebab-case and never rename it.

| `ActionStateAppearance` | Effect | `null` means |
| --- | --- | --- |
| `Label` | Button text in this state. `""` shows no text. | The state's own `Label`. |
| `BackgroundColor` | `#rrggbb`. | The button's default. |
| `LabelColor` | `#rrggbb`. | The button's default. |
| `IconId` | Icon in the host's icon id format. | No icon. |

The appearance is applied **only to a state a button adopts for the first time**. Re-reading the provider
never restyles a state the user has already configured. Set `IconId` only to an id the host can resolve:
an unknown id is stored as given and renders as no icon, which the user then has to clear by hand.

For labels, reuse the `MacroDeckStrings.States` family (`On`, `Off`, `Muted`, `Unmuted`, `Visible`,
`Hidden`, `Recording`, `Streaming`, `Playing`, `Paused`, `Unavailable` and more) instead of shipping your
own copies - see [Reuse `MacroDeckStrings`](/features/localization/#reuse-macrodeckstrings-instead-of-duplicating-common-strings).

## Reading state

```csharp
public Task<ActionStateSnapshot?> GetActionStateAsync(
	IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
{
	// Half-typed drafts arrive here too: a missing value is not an error.
	if (parameters.GetValueOrDefault("scene") is not string scene || scene.Length == 0)
	{
		return Task.FromResult<ActionStateSnapshot?>(null);
	}

	var active = _obs.CurrentScene == scene ? "active" : "inactive";
	return Task.FromResult<ActionStateSnapshot?>(new ActionStateSnapshot(_states, active));
}

public TimeSpan StatePollInterval => TimeSpan.FromSeconds(1);
```

`GetActionStateAsync` runs on two cadences. It runs once per settled editor draft while the user
configures the action, with a partial parameter set, so it must not throw. It also runs every
`StatePollInterval` while a button follows the instance.

- **Side-effect free.** Answer from state you already hold, honour the token, and never connect or
  authenticate to produce an answer.
- **`null` means "nothing to report"** - unconfigured, disconnected, gone. `ActiveStateId = null` means
  the set is known but no state is active. A returned snapshot's `States` is never empty.
- **The set may change.** Returning a different set is how live conditions are reported. The host
  re-adopts by id and keeps the appearance of every id that survived.
- **Prefer an `unavailable` state to `null` on a transient failure.** The host turns an exception into a
  `null` snapshot, but a player that is momentarily unreachable still has a known state set. Mapping the
  failure to your own `unavailable` state tells the user more.

`StatePollInterval` (default two seconds) is a request. The host clamps it and reads less often, or not
at all, while nothing displays the button. See
[the state-poll window](/reference/capability-parity/#the-state-poll-window).

## Bridging the poll delay

```csharp
public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
{
	var wasMuted = _client.IsMuted;
	await _client.SetMutedAsync(!wasMuted, context.CancellationToken);
	return ActionResult.Success(wasMuted ? "unmuted" : "muted");
}
```

A button follows its provider within about one poll interval, not instantly. When the action is also
the button's provider, its executor can return `Success(expectedStateId)` or
`Accepted(message, expectedStateId)` to name the state it expects next. The host may show that state
briefly while it waits for a read to confirm it. The id must be one `GetActionStateAsync` advertises.
Failures and results without an expected id keep normal polling.

## Over the plugin protocol

| Operation | SDK member |
| --- | --- |
| `state` | `GetActionStateAsync` with the instance's parameters |
| `execute` | carries `ExpectedStateId` in the execute result |

A plugin cannot push a state change for one instance, because a configured instance has no wire identity.
A `state.update` for the actions kind only brings the next read forward. Test it with
`PluginTestHarness.Actions.GetActionStateAsync(localId, parameters)`.

## See also

- [Actions](/features/actions/)
- [Button icons](/features/button-icons/) - a separate capability; enabling one never enables the other
- [Capability parity](/reference/capability-parity/#the-state-poll-window)
- [Localization](/features/localization/)
