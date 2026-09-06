---
title: Progress
description: macrodeck.progress-bar and macrodeck.progress-text draw a position that keeps moving, resolved by the reader from one shared reference shape.
---

`macrodeck.progress-bar`, `macrodeck.progress-text`

## Purpose

A playback position has the same problem a clock does, one step further along: it changes every second
*and* it depends on where playback had reached. Both elements carry a `UiProgressReference` -
`{"$progress":{"positionMs":42000,"durationMs":215000,"anchor":"2026-08-25T12:00:00.000Z"}}` on the wire -
which says where the medium was at one instant and how fast it has been moving since. The reader resolves
`clamp(positionMs + (t - anchor) * rate, 0, durationMs)` on its own synchronised clock, re-evaluating at
least once a second, so a playing track costs no patch between track changes.

This is why the two are `macrodeck.*` rather than `ui.*`: a reader cannot draw either from the tree alone.
It has to know what `$progress` means and advance it on its own clock - see the family split on
[Components](/sdk/ui/components/).

They share one page because they share the one reference shape and the one governing ADR
([0065](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0065-the-component-profile-authoring-contracts.md)).
`macrodeck.progress-bar` draws the position as a track; `macrodeck.progress-text` draws one of its
derivations as a run.

`rate` absent means normal speed - the ordinary case, kept key-free - and a halted medium carries `0`, so
"paused" is a value rather than something a reader infers. `durationMs` absent means unknown: a bar draws
an empty track and `elapsed`/`remaining`/`duration` resolve to nothing rather than the reader inventing a
whole.

## Properties

### `macrodeck.progress-bar`

| Property | Meaning | Absent means |
|---|---|---|
| `value` | The progress reference the reader resolves | An empty track is drawn |
| `startColor` | The colour at the start of the filled span, as `#rrggbb` | **The reader's own accent colour** |
| `endColor` | The colour at the head of the filled span, as `#rrggbb` | **The reader's own accent colour** |
| `thickness` | The track's thickness on the cross axis, a length | Left to the reader |

`macrodeck.progress-bar`'s geometry is `ui.range-bar`'s, exactly, with the span always beginning at the
track's start and no marker - the two draw the same picture, and only where the end of the span comes from
differs. The end is `position(t) / durationMs`, clamped to `0..1`, and it is `0` when the reference carries
no `durationMs` - a fraction of an unknown whole is not a number a reader may invent. See
[Range bar](/sdk/ui/components/range-bar/) for the shared geometry.

### `macrodeck.progress-text`

| Property | Meaning | Absent means |
|---|---|---|
| `value` | The progress reference the reader resolves | Nothing is drawn |
| `format` | Which derivation to show - `elapsed`, `remaining` or `duration` | A reader draws nothing for a format it does not know |
| `size` | The font size, a length | Left to the reader |
| `minSize` | The floor `size` may shrink to so the run fits its box | The run never shrinks; it ellipsizes instead |
| `weight` | The font weight | `regular` |
| `role` | The semantic colour | `primary` |
| `align` | Alignment within the run's own box | `start` |

Every format is a duration, not a clock time, so none of them takes a zone and none is drawn the way
`macrodeck.dynamic-text`'s `time` format is - a duration has no day period and no hour cycle to choose
between. A reader draws hours only when the duration reaches one, so a three-minute track reads `3:07`
rather than `0:03:07`, and pads every segment below the leading one to two digits. `remaining` and
`duration` are empty when the reference carries no `durationMs`, so a caption bound to either disappears
rather than counting down from nothing or showing a whole that does not exist. Digits are drawn with equal
advance width, so the run does not shift sideways as it counts.

## Supported children

Neither carries children. Both are leaves.

## Events and interactions

Neither declares events. Both are read-only - drag the same position with `ui.slider` instead.

## Layout behaviour

`macrodeck.progress-bar` follows `ui.range-bar`'s layout: the track spans the element's full main-axis
extent and is `thickness` tall on the cross axis. `macrodeck.progress-text` sizes exactly like `ui.text`:
its box is its font size, with a line height of one. Both follow the ordinary leaf rule on their parent
stack's main axis - `mainSize` or `fill` if declared, otherwise content extent. See
[Sizing](/sdk/ui/concepts/sizing/).

## Example

```csharp
new UiProgressBar
{
    Key = "progress",
    Value = UiValue.Of(UiProgressReference.Advancing(42_000, anchor, durationMs: 215_000)),
    Thickness = 0.05,
    Fallback = new UiRangeBar { Key = "progressFallback", /* frozen at the anchor position */ },
}
```

```csharp
new UiProgressText
{
    Key = "remaining",
    Value = UiValue.Of(UiProgressReference.Advancing(42_000, anchor, durationMs: 215_000)),
    Format = UiProgressFormats.Remaining,
    Fallback = new UiTextRun { Key = "remainingFallback", /* the duration composed for the anchor */ },
}
```

Give both a `Fallback`, for the reason above: a `ui.range-bar` and a `ui.text` holding the value at the
anchor, so a reader too old for either shows the right picture at the wrong second rather than nothing.
