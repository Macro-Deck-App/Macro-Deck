---
title: Dial
description: A rotary level the user turns, the interactive counterpart of the gauge.
---

A rotary level: a [gauge](/ui/components/gauge/)'s arc with a thumb the user turns. Like a
[slider](/ui/components/slider/) it holds a fraction of the sweep in `0..1`, not a value in your own units.

`ui.dial`

## Example

```csharp
new UiDial
{
    Key = "volume",
    Level = UiValue.From(() => state.Value.Volume / 100.0),
    Step = 0.05,
    Thickness = 0.08,
    Events =
    [
        UiEventHandler.On(UiComponentEvents.Adjust, data => Preview(data)),
        UiEventHandler.On(UiComponentEvents.Change, data => Apply(data)),
    ],
    Fallback = new UiSlider
    {
        Key = "volumeFallback",
        Level = UiValue.From(() => state.Value.Volume / 100.0),
        Step = 0.05,
        Events =
        [
            UiEventHandler.On(UiComponentEvents.Adjust, data => Preview(data)),
            UiEventHandler.On(UiComponentEvents.Change, data => Apply(data)),
        ],
    },
}
```

![A three-quarter arc open at the bottom, filled in blue to 60 percent, with a white thumb at the end of the fill](../../../../assets/ui/dial.png)

`adjust` and `change` carry the level as a bare number and behave exactly as on a slider - see
[Reading the level](/ui/components/slider/#reading-the-level) and
[Adjust or change](/ui/components/slider/#adjust-or-change).

## Turning past the ends

The level follows the pointer's angle about the centre of the box, and never jumps across the ends of the
sweep. Once it reaches `0` or `1` it stays there while the pointer carries on past the end - through the gap
at the bottom, or on around a full ring - and follows again as soon as the pointer turns back inside the
sweep. A drag that overshoots the maximum never lands on the minimum.

A press that begins in the gap takes the nearer end. Near the centre the angle means nothing, so a pointer
within `0.2` of the radius from the centre keeps the current level.

## Properties

| Property | Values | Default (absent) | Meaning |
|---|---|---|---|
| `Level` (`level`) | `0..1` | `0` | The filled fraction of the sweep. |
| `Step` (`step`) | fraction of the sweep | Continuous | The granularity the level snaps to, as on a slider. |
| `StartAngle` (`startAngle`) | `double`, degrees | `-135` | Where the arc begins, clockwise from twelve o'clock. |
| `EndAngle` (`endAngle`) | `double`, degrees | `135` | Where the arc ends. |
| `LevelColor` (`levelColor`) | `#rrggbb` | The reader's own accent colour | The filled arc's colour. |
| `Thickness` (`thickness`) | length | Left to the reader | The arc's width; the thumb scales with it. |
| `MainSize` (`mainSize`), `Fill` (`fill`) | - | - | Shared with every leaf - see [Sizing](/ui/concepts/sizing/). |

## Events

| Event | Fires when | Payload |
|---|---|---|
| `adjust` (`UiComponentEvents.Adjust`) | An intermediate level while the user is still turning | The level, a bare number |
| `change` (`UiComponentEvents.Change`) | The interaction ended, sent once | The level, a bare number |

## Children

None. `ui.dial` is a leaf.

## Layout

The element's whole box is the interactive surface. A dial has no content extent: on its parent's main axis
it takes `MainSize` or `Fill`, and without either it is `0` long. See [Sizing](/ui/concepts/sizing/).

## Reader behaviour

- **Geometry:** the arc and fill exactly as [`ui.gauge`](/ui/components/gauge/#reader-behaviour) draws them,
  plus a thumb disc of radius `1.25 * thickness` on the arc at the level, in the primary text colour, ringed
  by `0.28 * thickness` in the widget's own background colour.
- **Interaction only where declared.** A dial with no events is drawn and cannot be touched.
- **Pointer mapping:** the pointer's angle about the box centre, projected onto the sweep and tracked
  continuously during a drag. The level is clamped to the sweep: at `0` or `1` it stays while the pointer
  goes further and moves again only once the pointer comes back inside. A press that begins outside the
  sweep takes the nearer end. Within `0.2 * radius` of the centre the level does not change.
- **A zero sweep** offers no interaction.
- **Snapping, painting locally first, rate and order** are those of [`ui.slider`](/ui/components/slider/#reader-behaviour):
  `adjust` at most ten times a second, never after the `change` that ended the interaction.
- **Keyboard and hardware:** activating a tile from the keyboard does nothing to a dial. A deck's rotary
  encoders (layout regions of kind `LayoutRegionKinds.Encoder`) are not routed to widgets yet, so a physical
  knob does not turn a dial.
- A reader that does not know `ui.dial` draws the node's `fallback`: a `ui.slider` with the same events, or
  a `ui.range-bar` at the same level when the dial declares none:

```json
{
  "type": "ui.dial",
  "properties": { "level": 0.6, "step": 0.05, "events": ["adjust", "change"] },
  "fallback": {
    "type": "ui.slider",
    "properties": { "level": 0.6, "step": 0.05, "events": ["adjust", "change"] }
  }
}
```

## See also

- [Gauge](/ui/components/gauge/)
- [Slider](/ui/components/slider/)
- [Events](/ui/concepts/events/)
