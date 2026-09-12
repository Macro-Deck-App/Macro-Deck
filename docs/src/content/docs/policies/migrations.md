---
title: Migrations
description: Migration guides between Macro Deck SDK and protocol majors - what changed, what to use instead, and how long the previous version stays negotiable.
---

What to change in a plugin when the SDK or the plugin protocol moves a major. Importing a user's Macro
Deck 2 settings is a different thing - see [settings migrations](/features/settings-migrations/).

| Step | What changed | You must act? | Flagged by |
| --- | --- | --- | --- |
| [Protocol 1 to 2](#protocol-1-to-2-widget-states-are-addressed-by-id) | Widget appearance names states by stable id | No - deprecated in `3.0.0`, removed in `4.0.0` | [MDP5002](/reference/analyzers/#mdp5002) |
| [Protocol 2 to 3](#protocol-2-to-3-descriptor-text-may-be-localized) | Descriptor text may be a localization reference | No | - |

## Protocol 1 to 2: widget states are addressed by id

**What changed:** a widget appearance change names the states it applies to by stable id instead of the
fixed `Current` / `On` / `Off` / `Both` selector. Nothing else on the wire changed, and **major `1`
remains negotiable** - a plugin that speaks only `1` keeps working against a host that speaks `2`, with the
emulation [below](#what-a-protocol-1-plugin-sees).

An Action Button can now have any number of states, each with an id separate from its display label, so
`On` and `Off` no longer name anything a three-state button has. See
[ADR 0056](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0056-widget-state-is-addressed-by-stable-state-id.md).

Nothing here is urgent. Do it when you next touch widget appearance code, or when the compatibility report
in the desktop app says your plugin is affected.

| Deprecated in `3.0.0`, removed in `4.0.0` | Use instead |
| --- | --- |
| `WidgetStateSelector` | Stable state ids, or the `WidgetStates.Current` / `WidgetStates.All` sentinels |
| `WidgetAppearanceRequest.State` | `WidgetAppearanceRequest.StateIds` |
| `WidgetTargetInfo.HasOnOffStates` | `WidgetTargetInfo.States` |

All three keep working until `4.0.0`. Recompiling reports each call site as
[MDP5002](/reference/analyzers/#mdp5002); with warnings as errors that is a build break at your chosen
warning level, but the API itself still works.

### Before and after

```diff
 var target = widgets.GetWidgets().Single(w => w.Id == widgetId);
-if (target.HasOnOffStates)
+if (target.States.Count > 1)
 {
     await widgets.ApplyAsync(new WidgetAppearanceRequest
     {
         WidgetId = widgetId,
-        State = WidgetStateSelector.Both,
+        StateIds = [WidgetStates.All],
         Patch = new WidgetAppearancePatch { BackgroundColor = "#00ff00" }
     });
 }
```

| Old | New |
| --- | --- |
| `State = WidgetStateSelector.Current` | `StateIds = [WidgetStates.Current]` - the default: whichever state is showing |
| `State = WidgetStateSelector.Both` | `StateIds = [WidgetStates.All]` - every state |
| `State = WidgetStateSelector.On` / `Off` | `StateIds = ["on"]`, or whatever id `GetWidgets()` reported, such as `["muted"]` |
| `HasOnOffStates` | `States.Count > 1` |

`WidgetTargetInfo` now reports the states a widget has and the one it is showing:

```csharp
foreach (var state in target.States)      // empty when the widget has one appearance
{
    Console.WriteLine($"{state.Id} = {state.Label}");
}

var showing = target.CurrentStateId;      // null when the widget has one appearance
```

Rules:

- Address a state by its **id**, never by its label or position. A user can rename and reorder states at
  any time; the id survives both.
- An id the widget does not have is dropped. A request naming no state the widget has changes nothing and
  returns `false` - a plugin cannot create a state this way.
- If you set both `StateIds` and the deprecated `State`, `StateIds` wins. Leaving both at their defaults
  means the current state.

### What a protocol 1 plugin sees

Nothing to do. A session negotiated at major `1` keeps sending the old payload, and the host maps it onto
real states:

| Old selector | What it changes on the new host |
| --- | --- |
| `Current` | The state the widget is showing. Unchanged. |
| `Off` | The state whose id is literally `off`; failing that, the first state; failing that, nothing. |
| `On` | The state whose id is literally `on`; failing that, the second state; failing that, nothing. |
| `Both` | **Every** state the widget has, including a third and beyond. |
| `HasOnOffStates` | Whether the widget has more than one state. |

A button carried over from the two-appearance model keeps the literal ids `off` and `on`, so `Off` and
`On` land exactly where they always did. On a button whose states came from an action (`Playing` /
`Paused` / `Stopped`, say), `Off` and `On` fall back to the first and second state, and states past the
second are invisible and unreachable until you move to major `2`. Nothing fails.

### Actions can now supply a button's states

New in this release and **available in protocol major `1` as well** - no need to move to `2`. An action
that knows a live state implements `IStateProviderActionDefinition`:

```csharp
internal sealed class ToggleMuteAction : IActionDefinition, IStateProviderActionDefinition
{
    public Task<ActionStateSnapshot?> GetActionStateAsync(
        IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        if (!_client.IsConnected)
        {
            return Task.FromResult<ActionStateSnapshot?>(new ActionStateSnapshot(
                [new("unmuted", "Unmuted"), new("muted", "Muted"), new("unavailable", "Unavailable")],
                "unavailable"));
        }

        return Task.FromResult<ActionStateSnapshot?>(new ActionStateSnapshot(
            [new("unmuted", "Unmuted"), new("muted", "Muted"), new("unavailable", "Unavailable")],
            _client.IsMuted ? "muted" : "unmuted"));
    }
}
```

| Rule | Why |
| --- | --- |
| Answer from the configured `parameters`, not from a widget | The same action can sit on many buttons with different configuration; each answers for itself |
| Keep state ids stable across reconfiguration | Users style appearance per id; a changed id orphans it |
| Be side-effect free and quick; tolerate a partly filled parameter set, never throw | It is polled while the button is on screen, and called while the user is still typing the configuration |
| Declare your own "cannot tell" state instead of returning `null` when you know the set but not the value | Users can style "disconnected" apart from "connected and off". Return `null` only when nothing is known |

See [capabilities](/features/) for the full contract.

## Protocol 2 to 3: descriptor text may be localized

**What changed:** descriptor text - action names, parameter labels, config-flow text - may be a
`{"$localized":…}` reference instead of a plain string. Below major `3` a plugin must send a plain string.
Earlier majors stay negotiable, and the host translates for them.

If you use `MacroDeck.Plugin.Hosting`, there is nothing to change: against a host older than protocol `3`
the SDK resolves descriptor text in your default language before sending it. If you implement the protocol
yourself, send plain strings unless the session negotiated `3` or higher. See
[localization](/features/localization/) and [the protocol reference](/reference/protocol/).

## When a migration guide appears

Only for a **major** release, the only release in which anything frozen may change:

| Trigger | Scope of the guide |
| --- | --- |
| An SDK major in which at least one API reaches the removal version its `[MacroDeckDeprecated]` declared | That release's `RemovedIn` list - exactly the APIs that disappear in it, no others |
| A protocol major (an incompatible wire change) | A window rather than a cliff: the previous major stays negotiable for as long as the host's supported range says |
| A manifest or artifact format version, if `manifestVersion` ever advances beyond `1` | The format change |

Minor and patch releases never get one: they may add and deprecate, but never remove or change anything
frozen. See [the compatibility policy](/policies/compatibility/).

## What a migration guide contains

Each guide covers one major-to-major step:

- **What was removed**, by name, with the version it was deprecated in and removed in - the same entries
  the deprecation registry carries, so the two cannot disagree.
- **What to use instead**, per removed API, from the `Replacement` and guidance its `[MacroDeckDeprecated]`
  declared.
- **Behaviour changes** that are not removals: a changed default, a tightened validation rule, a changed
  error-code meaning.
- **Protocol changes**, if the protocol major moved: new or removed message types, changed payload shapes,
  and which versions remain negotiable.
- **How to check your plugin**: the analyzer diagnostics that flag each case, and the conformance checks
  that fail if you missed one.

## What to do meanwhile

- **Reference `MacroDeck.Plugin.Analyzers`.** Deprecated-API use is [MDP5002](/reference/analyzers/#mdp5002)
  at every call site, with the removal version and replacement in the message, long before the removal.
  Use of an API whose removal version this SDK has already reached is the error
  [MDP5004](/reference/analyzers/#mdp5004).
- **Read the compatibility report** the host returns in the session response, and the Compatibility tab in
  the desktop app. `update_recommended` or `update_required` is the earliest signal that a migration is
  coming for your plugin specifically.
- **Do not hard-code advertised values.** Limits, timeouts and the protocol version range are published at
  run time; hard-coding them turns a non-breaking change into a migration for yourself.

## See also

- [Compatibility policy](/policies/compatibility/) - what is frozen, what counts as a break, and how majors
  are negotiated.
- [Deprecations](/policies/deprecations/) - the lifecycle, the registry, and how the host tells confirmed use
  from a guess.
