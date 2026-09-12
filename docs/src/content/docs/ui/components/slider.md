---
title: Slider
description: ui.slider is a draggable level - a rounded track carrying a filled span, and the interactive counterpart of ui.range-bar.
---

`ui.slider`

## Purpose

`ui.slider` is a draggable level: a rounded track carrying a filled span, and the interactive counterpart
of `ui.range-bar`. It carries a **level** - a fraction of its track in `0..1` - rather than a value in your
own units, and the number beside it is an ordinary `ui.text` you patch yourself.

The fraction is what makes the control feel local: a reader snaps and paints the level it computed
straight away and reconciles with the producer afterwards, so a drag never waits for a round trip. It also
keeps your units - a track duration, a decibel, a percentage - out of every renderer, along with the job of
formatting them.

## Properties

| Property | Meaning | Absent means |
|---|---|---|
| `level` | The filled fraction of the track, `0..1` | `0` |
| `step` | The granularity `level` snaps to, in the same fraction space | The level is continuous |
| `levelColor` | The filled span's colour, as `#rrggbb` | **The reader's own accent colour** |
| `direction` | The axis the level travels along - `horizontal` or `vertical` | `horizontal` - unlike `ui.stack`, where absent means vertical |
| `thickness` | The drawn track's thickness on the cross axis, a length | Left to the reader |

`levelColor` is a literal colour rather than a role for the same reason a range bar's `startColor` is - see
[Colours and text](/ui/concepts/theming/). Its absent meaning is spelled by omitting the key rather
than by a role, because a colour vocabulary with one member is an absence with extra steps.

When `step` is present, the level is snapped to `round(level / step) * step`, clamped to `0..1`, before it
is painted or sent. A tie rounds up - stated because the obvious rounding primitive differs by platform,
and a reader that rounded half to even would paint one grid point while the producer acted on its
neighbour.

`direction` is the axis the level travels along, not the axis the element occupies in its parent stack.
`vertical` runs **bottom to top** - up is more.

## Supported children

None. `ui.slider` is a leaf.

## Events and interactions

**Interaction is offered only where it is declared.** A node whose `events` is empty carries no `events`
property at all and is drawn as a level the user cannot touch - there is deliberately no separate disabled
property to contradict it.

| Event | When | Payload |
|---|---|---|
| `adjust` | An intermediate level, while the user is still working the control | The level, as a bare number |
| `change` | The level the interaction ended on, sent once | The level, as a bare number |

A reader sends `adjust` no more than ten times a second, and never after the `change` that ended the
interaction; it sends only the names the node declares. That split is how a producer takes the value the
user landed on without freezing the display on the way there: act on `change` alone and let `adjust` move
the level. A seek slider that acted on every intermediate value would drag the audio through every
waypoint between here and the target; one that ignored them entirely would leave the user pushing a
control that does not move. See [Events](/ui/concepts/events/) for the shared interaction model.

## Layout behaviour

The element's **whole box** is the interactive surface, and the drawn track is smaller than it - not an
oversight, but the point: a pill a few units thick on a deck tile is not a target a thumb can hit, so a
reader that made only the track draggable would draw the right picture and ship an unusable control. A
pointer anywhere in the box sets the level to its position projected onto `direction`, clamped to `0..1`;
the cross-axis position is ignored, and a pointer that leaves the box mid-drag keeps controlling the
element until it is released.

`thickness` only sizes the drawn track, not the element. On its parent stack's main axis the slider follows
the ordinary rule - `mainSize` or `fill` if declared, otherwise its content extent. See
[Sizing](/ui/concepts/sizing/).

The track and the thumb are normative geometry a reader implements exactly - the fixtures in
`ui-model/fixtures/component-profile/` resolve both at two sizes.

## Example

```csharp
new UiSlider
{
    Key = "track",
    Level = UiValue.From(() => (state.Value.Volume - min) / (max - min)),
    Step = UiValue.From(() => step / (max - min)),
    Thickness = UiSize.FromBasis(0.12, 0.55),
    Fill = true,
    Events =
    [
        UiEventHandler.On(UiComponentEvents.Adjust, data => Apply(data, push: false)),
        UiEventHandler.On(UiComponentEvents.Change, data => Apply(data, push: true)),
    ],
}
```
