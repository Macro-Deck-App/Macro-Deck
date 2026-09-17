---
title: Slider
description: A draggable level on a rounded track, the interactive counterpart of the range bar.
---

A draggable level: a rounded track carrying a filled span. It holds a fraction of the track in `0..1`,
not a value in your own units.

`ui.slider`

## Example

```csharp
new UiSlider
{
    Key = "volume",
    Fill = true,
    Level = UiValue.From(() => state.Value.Volume / 100.0),
    Step = 0.05,
    Thickness = UiSize.FromBasis(0.12, 0.55),
    Events =
    [
        UiEventHandler.On(UiComponentEvents.Adjust, data => Preview(data)),
        UiEventHandler.On(UiComponentEvents.Change, data => Apply(data)),
    ],
}
```

![A wide tile with a horizontal slider filled in blue to 65 percent and a white thumb at the level](../../../../assets/ui/slider.png)

A horizontal volume track snapping to 5 % steps: `adjust` moves the display while dragging, `change`
commits the level the user landed on.

## Reading the level

```csharp
UiEventOutcome Apply(UiEventData data)
{
    if (!data.TryGetDouble(out var level))
    {
        return UiEventOutcome.Rejected("The event payload is not a number.");
    }

    state.Set(state.Value with { Volume = level * 100 });
    return UiEventOutcome.Accepted;
}
```

Both events carry the level as a bare number. Convert it to your own units yourself; a number beside the
slider is an ordinary `ui.text` you update.

## Adjust or change

Act on `change` alone for anything costly, such as a seek, and let `adjust` just move the level. Acting on
every `adjust` would drag the target through every waypoint; ignoring both would leave a control that does
not move.

## Vertical slider

```csharp
Direction = UiComponentDirections.Vertical,
```

![A tall tile with a vertical slider filled in blue from the bottom to 40 percent, the thumb at the top of the fill](../../../../assets/ui/slider-vertical.png)

`Direction` is the axis the level travels along, not the element's axis in its parent. Vertical runs
bottom to top - up is more.

## Relative drag

```csharp
Interaction = UiComponentSliderInteractions.Relative,
```

By default a press jumps the level to the pointer. With `Interaction` set to `relative`, a press leaves the
level where it is, and the level then moves by how far the pointer travels: a travel of the whole box
length spans the whole `0..1` range, in either direction, clamped at both ends. Use it where users nudge a
value, such as a volume, and a jump to the touch point would throw it far off. A tap moves nothing and
sends neither `adjust` nor `change`.

A reader that predates `interaction` ignores it and keeps the absolute behaviour, so the slider still
works, only without the grab.

## Colour and fallback

```csharp
LevelColor = "#2b6cee",
Fallback = new UiRangeBar { Key = "trackFallback", Start = 0, End = level, StartColor = "#2b6cee", EndColor = "#2b6cee" },
```

A reader without `ui.slider` draws the fallback, here the same level as a read-only range bar.

## Properties

| Property | Values | Default (absent) | Meaning |
|---|---|---|---|
| `Level` (`level`) | `0..1` | `0` | The filled fraction of the track. |
| `Step` (`step`) | fraction of the track | Continuous | The granularity the level snaps to. |
| `LevelColor` (`levelColor`) | `#rrggbb` | The reader's own accent colour | The filled span's colour. |
| `Direction` (`direction`) | `horizontal`, `vertical` | `horizontal` - unlike `ui.stack` | The axis the level travels along. |
| `Interaction` (`interaction`) | `relative` | Absolute: the level jumps to the pointer | How a pointer maps to the level - see [Relative drag](#relative-drag). An unknown value reads as absent. |
| `Thickness` (`thickness`) | length | Left to the reader | The drawn track's thickness on the cross axis. |

`LevelColor` is a literal colour, not a theme role - see [Colours and text](/ui/concepts/theming/).

## Events

| Event | Fires when | Payload |
|---|---|---|
| `adjust` (`UiComponentEvents.Adjust`) | An intermediate level while the user is still working the control | The level, a bare number |
| `change` (`UiComponentEvents.Change`) | The interaction ended, sent once - not for a relative tap, see [Reader behaviour](#reader-behaviour) | The level, a bare number |
| `double-press` (`UiComponentEvents.DoublePress`) | A second tap completed shortly after the first, neither one a drag | None |

Declare `double-press` for an action on a double tap, such as resetting to a home level. Each tap is still an
ordinary interaction and sends its own `change` first, so the handler sees the level the second tap set and
replaces it. A reader that predates `double-press` never sends it, and the taps stay plain level changes. On a
relative slider a tap moves nothing, so `double-press` arrives on its own. Unlike on a [button](/ui/components/button/#reader-behaviour),
nothing is held back while waiting for a second tap.

## Children

None. `ui.slider` is a leaf.

## Layout

The element's whole box is the interactive surface; the drawn track is smaller. `Thickness` sizes only the
drawn track, not the element. On its parent's main axis the slider follows the ordinary rule - `MainSize`
or `Fill` if declared, otherwise its content extent. See [Sizing](/ui/concepts/sizing/).

## Reader behaviour

- **Interaction only where declared.** A node with no events carries no `events` property and is drawn as
  a level the user cannot touch; there is no disabled property.
- **The whole box takes input.** A pointer anywhere in it sets the level to its position projected onto
  `direction`, clamped to `0..1`; the cross-axis position is ignored. A pointer that leaves the box
  mid-drag keeps control until release.
- **Snapping:** with `step` present, the level becomes `round(level / step) * step`, clamped to `0..1`,
  before it is painted or sent. A tie rounds up.
- **Paint locally first:** the reader paints the level it computed straight away and reconciles with the
  producer afterwards.
- **Rate and order:** `adjust` at most ten times a second, never after the `change` that ended the
  interaction. Only declared names are sent.
- **Double tap:** with `double-press` declared, a tap released within 400 ms of the previous one and starting
  within 24 px of it, neither moving more than a few pixels, sends `double-press` right after its `change`.
  A drag or a cancelled gesture in between starts over.
- **Relative drag** (`interaction: relative`), overriding the input, rate-and-order and double-tap rules
  above where they differ: a press paints and sends nothing. Within the few pixels of tap slop the level does
  not move; past it, the level is the one at the press plus the pointer's travel along `direction` since the
  press, divided by the box's measured length on that axis (up is more when vertical), clamped to `0..1` after
  every move so reversing at an end moves back at once. `step` snaps as usual, and the level counts as moved
  only once that snapped level differs from the snapped level at the press. An interaction whose level never
  moved sends neither `adjust` nor `change`; one that moved sends `change` even if it ended where it began.
- **Geometry is normative:** the track and thumb are implemented exactly; the fixtures in
  `ui-model/fixtures/component-profile/` resolve both at two sizes.

## See also

- [Range bar](/ui/components/range-bar/)
- [Events](/ui/concepts/events/)
- [State and bindings](/ui/concepts/state-and-bindings/)
