---
title: Button
description: ui.button lays out children exactly as ui.stack does, and adds its own artwork, a ring, and a press.
---

`ui.button`

## Purpose

`ui.button` is a layout container the user presses. It lays its children out exactly as `ui.stack` does,
and adds three things a stack has no business carrying: artwork of its own behind the children, a ring
around the edge, and the press itself.

The artwork is the button's own property rather than a child, because a stack cannot say "this one fills
the box and the rest sit over it" - and because it keeps the tree's *shape* fixed while the face changes,
so a button that swaps its whole appearance is a `set-properties` patch rather than a rebuilt subtree.
Reach for `ui.button` to say "a tile the user presses", and for its artwork and its ring; reach for
`ui.stack` with a declared press when you want the interaction without either.

## Properties

| Property | Meaning | Absent means |
|---|---|---|
| `direction` | The layout axis - `vertical` or `horizontal` | `vertical` |
| `justify` | How free space is distributed along the main axis | `start` |
| `align` | How children are aligned on the cross axis | `stretch` |
| `gap` | The gap between children, a length | No gap |
| `padding` | Inner padding on every edge, a length | No padding |
| `background` | The button's face, as `#rrggbb` | **The reader's own accent colour** - a button always has a face, unlike a stack |
| `source` | Artwork drawn across the whole box behind the children | None |
| `transition` | How a change of `source` is drawn - `crossfade` | The new artwork simply replaces the old one |
| `fit` | How the artwork fills the box - `contain` or `cover` | `contain` |
| `zoom` | A multiplier scaling the artwork about its own centre, `0.1..4` | `1` |
| `offsetX` | The artwork shifted across, a fraction of the element's own width in `-1..1`, applied after `zoom` | `0` |
| `offsetY` | The same down the element's own height | `0` |
| `opacity` | How opaque the artwork is drawn, `0..1` | Fully opaque |
| `brightness` | A multiplier of the artwork's own luminance, `0..2` | `1` |
| `saturation` | A multiplier of the artwork's own saturation, `0..2` | `1` |
| `borderStyle` | How the ring is drawn - see below | No ring at all - there is deliberately no value spelling "off" |
| `borderColor` | The ring's colour, as `#rrggbb` | The style supplies its own colour |
| `corner` | How round the button's own corners are - `tile` | A fraction of the button's own height - see below |

A button rounds itself by `0.12` of its own height, so one half the height of another is half as round -
which is what keeps a small button from reading as a pill and a large one as a square. Two things override
that. A button that *is* the whole widget tree fills the tile and takes the tile's corner without saying
so, because a face that does not follow the tile it fills is the first thing a reader notices. And a
nested button can ask for the same corner with `corner: "tile"` - what a full-bleed backdrop needs, since
a fraction of its own height cuts an arc across the tile wherever the two disagree, and a view never
learns the height it will be drawn at. A reader that has never heard of `corner` ignores it and paints its
own corner, which still draws the button; that is why this is a property rather than a component version.

`background`'s absent meaning is the one place a button disagrees with a stack: a button always has a face,
so spelling that face's default as a role would freeze it against the reader's theme - see
[Colours and text](/ui/concepts/theming/).

`borderStyle` is one of `static`, `heartbeat`, `breathing`, `blink`, `comet`, `ants`, `hue-shift` or `rgb`.
The first six are tinted in `borderColor`; `hue-shift` and `rgb` cycle through colours of their own and
ignore it. The ring is drawn at a fixed `2` device-independent units along the inner edge, following the
element's corner radius - the one length in the framework that is not a fraction of the basis, deliberately:
a ring that grew with the element would read wildly inconsistently across differently sized buttons. Every
looping style is phase-locked to a clock shared with Macro Deck, so one button animates in step on every
client showing it, and it keeps animating whatever the viewer's reduced-motion preference says - a border
a deck owner configured to move is the same border on every client, so a reader must not freeze it.

Paint order, which none of the properties imply and a reader owes: `background`, then the artwork, then
the children, then the press feedback, then the ring. The ring is above everything so artwork covering the
whole box cannot hide it, and the press feedback sits below the ring for the same reason.

## Supported children

Any element, any number, laid out exactly as `ui.stack` lays out its children along `direction`.

## Events and interactions

**Interaction is offered only where it is declared**, the same rule `ui.slider` follows, and there is no
disabled property to contradict it. A button that declares no events is still drawn - its face is worth as
much unpressed - but it accepts nothing.

| Event | When |
|---|---|
| `press` | The user completed a press without holding it |
| `long-press` | The press was still held after 600 ms |
| `press-start` | The press began |
| `press-end` | The press ended, however it ended |

None carries a payload. `press` is the primary name: a reader that implements only one press name
implements this one, and the button's main flow runs correctly. A reader never infers `press` from a
`press-start`/`press-end` pair, sends `press` in an interaction where `long-press` already fired, or sends
a name the node did not declare - so declaring only `press` is how you say a button has no long-press
behaviour, rather than receiving one and discarding it. `press-end` follows every `press-start`, including
when the pointer left the element or the gesture was cancelled, so a producer driving something for as
long as the finger is down always gets its release.

A reader paints the press itself, immediately: it tints the whole element white at `0.2` alpha, fading in
over `20 ms` and out over `140 ms`, and holds it visible at least `60 ms` so a tap shorter than that still
registers. It never waits for the producer to answer before painting - the round trip runs the flow, it
does not confirm the touch. See [Events](/ui/concepts/events/) for the shared interaction model, and
the button conformance fixtures in `ui-model/fixtures/component-profile/` for the exact ordering a reader
owes.

## Layout behaviour

Identical to `ui.stack`: `direction`, `justify`, `align`, `gap` and `padding` divide the button's own
content box among its children exactly as they would on a stack. On its own parent's main axis a button
follows the ordinary leaf/container rule - `mainSize` or `fill` if declared, otherwise its content extent.
See [Sizing](/ui/concepts/sizing/).

## Example

```csharp
new UiButton
{
    Key = "button",
    Justify = UiComponentJustify.Center,
    Background = UiValue.From(() => state.Value.Face),
    Source = UiValue.From(() => state.Value.Icon),
    Fit = UiComponentImageFits.Cover,
    BorderStyle = UiComponentBorderStyles.Breathing,
    BorderColor = "#ff3b30",
    Events = [UiEventHandler.On(UiComponentEvents.Press, () => Run())],
    Children = [new UiTextRun { Key = "label", Text = UiText.Of("Mute"), Size = 0.14 }],
    Fallback = new UiStack { Key = "buttonFallback", /* the same layout and children */ },
}
```
