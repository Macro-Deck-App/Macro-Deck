---
title: Migrations
description: Migration guides between Macro Deck SDK and protocol majors - what changed, what to use instead, and how long the previous version stays negotiable.
---

## Protocol 1 to 2: widget states are addressed by id

Protocol major `2` changes one thing: how a widget appearance change names the state it applies to.
Everything else about the wire contract is unchanged, and **major `1` remains negotiable** — a plugin
that speaks only `1` keeps working against a host that speaks `2`, with the emulation described below.

Nothing here is urgent. Read it when you next touch widget appearance code, or when the compatibility
report in the desktop app tells you your plugin is affected.

### Why it changed

An Action Button used to have exactly two appearances, so the appearance API named them with a fixed
four-way selector — `Current`, `On`, `Off`, `Both` — and a `HasOnOffStates` flag. A button can now
have any number of states, each with a **stable id separate from its display label**, and an action
can supply that set itself. `On` and `Off` no longer name anything a three-state button has, so the
selector had to be replaced rather than extended. See
[ADR 0056](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0056-widget-state-is-addressed-by-stable-state-id.md).

### What was deprecated

Deprecated in `3.0.0`, removed in `4.0.0`. All three keep working until then.

| Deprecated | Use instead |
| --- | --- |
| `WidgetStateSelector` | Stable state ids, or the `WidgetStates.Current` / `WidgetStates.All` sentinels |
| `WidgetAppearanceRequest.State` | `WidgetAppearanceRequest.StateIds` |
| `WidgetTargetInfo.HasOnOffStates` | `WidgetTargetInfo.States` |

Recompiling against this SDK reports each call site as
[MDP5002](/sdk/analyzers/#mdp5002). If you build with warnings as errors, that is a build break
at your chosen warning level — the API itself still works.

### What to use instead

`WidgetTargetInfo` now reports the states a widget actually has, and which one it is showing:

```csharp
var target = widgets.GetWidgets().Single(w => w.Id == widgetId);

foreach (var state in target.States)      // empty when the widget has one appearance
{
    Console.WriteLine($"{state.Id} = {state.Label}");
}

var showing = target.CurrentStateId;      // null when the widget has one appearance
```

`WidgetAppearanceRequest` takes the ids to change. Two sentinels cover the cases where you do not
want to name one:

```csharp
await widgets.ApplyAsync(new WidgetAppearanceRequest
{
    WidgetId = widgetId,
    StateIds = [WidgetStates.Current],     // the default: whichever state is showing
    Patch = new WidgetAppearancePatch { BackgroundColor = "#00ff00" }
});

// every state, so the change is visible whichever one the button is in
StateIds = [WidgetStates.All]

// a specific state, by the id GetWidgets() reported
StateIds = ["muted"]
```

Address a state by its **id**, never by its label or its position. A user can rename a state at any
time and reorder the list; the id is what survives both. An id the widget does not have is dropped,
and a request naming no state the widget has changes nothing and returns `false` — a plugin cannot
create a state this way.

If you set both `StateIds` and the deprecated `State`, `StateIds` wins. Leaving both at their
defaults means the current state.

### What a protocol 1 plugin sees

You do not have to do anything. A session negotiated at major `1` keeps sending the old payload, and
the host maps it onto real states:

| Old selector | What it changes on the new host |
| --- | --- |
| `Current` | The state the widget is showing. Unchanged. |
| `Off` | The state whose id is literally `off`; failing that, the first state; failing that, nothing. |
| `On` | The state whose id is literally `on`; failing that, the second state; failing that, nothing. |
| `Both` | **Every** state the widget has, including a third and beyond. |
| `HasOnOffStates` | Whether the widget has more than one state. |

A button carried over from the two-appearance model keeps the literal ids `off` and `on`, so `Off`
and `On` land exactly where they always did. On a button whose states came from an action — `Playing`
/ `Paused` / `Stopped`, say — `Off` and `On` fall back to the first and second state, and states past
the second are invisible and unreachable until you move to major `2`. Nothing fails; you simply
cannot name what you cannot see.

### Actions can now supply a button's states

New in this release and **available in protocol major `1` as well** — you do not need to move to `2`
to use it. An action that knows a live state can offer it to the button it sits on:

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

Four things to get right:

- **Answer from the configured parameters, not from a widget.** The same action can sit on many
  buttons with different configuration, and each answers for itself.
- **Keep state ids stable across reconfiguration.** A user configures appearance per id; changing an
  id orphans what they styled.
- **Be side-effect free and quick.** It is polled while the button is on screen, and it is called
  while the user is still typing the action's configuration — so it must tolerate a partly filled
  parameter set rather than throwing.
- **Declare your own "cannot tell" state** rather than returning `null` when you know the set but not
  the value. A user can then style "disconnected" differently from "connected and off". Return `null`
  only when nothing is known at all.

See [capabilities](/sdk/capabilities/) for the full contract.

## When a migration guide appears

A migration guide is published for a **major** release only, because a major is the only release in
which anything frozen may change:

- **An SDK major**, when at least one API reaches the removal version its `[MacroDeckDeprecated]`
  declared. The guide is written against that release's `RemovedIn` list - the exact set of APIs that
  disappear in it, and no others.
- **A protocol major**, when the wire contract changes incompatibly. The previous major stays
  negotiable for as long as the host's supported range says it does, so a guide describes a window
  rather than a cliff.
- **A manifest or artifact format version**, if `manifestVersion` ever advances beyond `1`.

Minor and patch releases never get one: they may add and they may deprecate, but they may not remove or
change anything frozen. See [the compatibility policy](/policies/compatibility/).

## What a migration guide will contain

Each guide is scoped to one major-to-major step, and will cover:

- **What was removed**, by name, with the version it was deprecated in and the version it was removed
  in - the same entries the deprecation registry already carries, so the guide and the registry cannot
  disagree.
- **What to use instead**, per removed API, taken from the `Replacement` and guidance the
  `[MacroDeckDeprecated]` attribute declared.
- **Any behaviour change** that is not a removal: a changed default, a tightened validation rule, a
  changed error-code meaning.
- **Protocol changes**, if the protocol major moved: new or removed message types, changed payload
  shapes, and which versions remain negotiable.
- **How to check your own plugin** - the analyzer diagnostics that flag each case, and the conformance
  checks that fail if you missed one.

## What to do meanwhile

You are not waiting passively for a guide. The mechanisms that make a future migration small are
already running:

- **Reference `MacroDeck.Plugin.Analyzers`.** Deprecated-API use is flagged at every call site as
  [MDP5002](/sdk/analyzers/#mdp5002), with the removal version and the replacement in the
  message, long before the removal happens. Use of an API whose removal version this SDK has already
  reached is the error [MDP5004](/sdk/analyzers/#mdp5004).
- **Read the compatibility report** the host returns in the session response, and the Compatibility tab
  in the desktop app. A state of `update_recommended` or `update_required` is the earliest honest
  signal that a migration is coming for your plugin specifically.
- **Do not hard-code advertised values.** Limits, timeouts and the protocol version range are published
  at run time so a plugin reads them; a plugin that hard-codes them turns a non-breaking change into a
  migration for itself.

## See also

- [Compatibility policy](/policies/compatibility/) - what is frozen, what counts as a break, and how
  majors are negotiated.
- [Deprecations](/policies/deprecations/) - the lifecycle, the registry, and how the host tells
  confirmed use from a guess.
