---
title: Time and clock
description: macrodeck.dynamic-text and macrodeck.clock-dial draw a time the reader resolves itself from one shared reference shape.
---

`macrodeck.dynamic-text`, `macrodeck.clock-dial`

## Purpose

Both draw a time the reader resolves itself, rather than a time the producer already formatted. Each
carries a `UiTimeReference` - `{"$time":{"zone":"America/New_York"}}` on the wire - which means *now, as
the reader sees it*: the tree is built once and every renderer advances the display against its own
synchronised clock. A ticking view therefore costs no patch and no message, and keeps running while the
connection is down.

This is why the two are `macrodeck.*` rather than `ui.*`: a reader cannot draw either from the tree alone.
It has to know what `$time` means and advance it on its own clock - see the family split on
[Components](/sdk/ui/components/).

They are documented on one page because they share the one reference shape and the one governing ADR
([0065](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0065-the-component-profile-authoring-contracts.md)).
`macrodeck.dynamic-text` says *which* instant and how to draw it as a run; `macrodeck.clock-dial` draws the
same reference as an analogue face. Splitting the two is what lets a digital run and a dial share one value
shape rather than each inventing its own.

Never compose a time out of several nodes. Separator, digit system, writing direction and the position of
the day period all come from the reader's own language, so a producer that assembles the pieces itself
gets Korean and right-to-left languages wrong. Where a producer does need a particular shape - a deck is
a fixed display its owner arranges, and a clock set to a 12-hour face has to stay one on every device it
is shown on - it picks a `format` that names that shape, never a pattern of its own.

## Properties

### `macrodeck.dynamic-text`

| Property | Meaning | Absent means |
|---|---|---|
| `value` | The time reference the reader resolves | Nothing is drawn |
| `format` | Which derivation to show - see the table below | A reader draws nothing for a format it does not know |
| `seconds` | Whether the run includes seconds | It does not |
| `size` | The font size, a length | Left to the reader |
| `minSize` | The floor `size` may shrink to so the run fits its box | The run never shrinks; it ellipsizes instead |
| `weight` | The font weight | `regular` |
| `role` | The semantic colour | `primary` |
| `color` | A literal run colour, `#rrggbb`; overrides `role` | The `role` colour |
| `align` | Alignment within the run's own box | `start` |

Every `time-*` format draws the seconds the way `time` does, and that is normative: they are drawn,
together with the separator in front of them, at `0.55` of the run's own `size` and in the `muted` role,
while everything else in the run uses the node's own size and role - including when `color` is set, since
the seconds' own treatment is part of the format rather than a colour anyone chose. Digits in every format
are drawn with equal advance width, so the run does not shift sideways as it counts.

### `format` values

| value | what the run shows |
|---|---|
| `time` | The clock time, its hour cycle and shape chosen by the reader's own language |
| `time-12h` | A 12-hour face with the reader's day period; hour `1`-`12` |
| `time-12h-padded` | The same, hour `01`-`12` |
| `time-24h` | A 24-hour face, no day period; hour `00`-`23` |
| `time-24h-unpadded` | A 24-hour face, hour `0`-`23` |
| `date` | An abbreviated weekday, day and month, ordered and punctuated by the reader's language |
| `date-day-first` | `31/12/25` - day, month, two-digit year, each padded, `/`-separated |
| `date-month-first` | `12/31/25` |
| `date-iso` | `2025-12-31` - ISO 8601 field order, four-digit year |
| `date-long` | The full weekday and month names with the day, ordered by the reader's language |
| `zone-name` | The last segment of the reference's IANA id with underscores replaced by spaces, so `America/New_York` reads `New York` |
| `zone-offset` | The zone's offset from UTC at that instant, `UTC+02:00`, daylight saving included |

`zone-name` and `zone-offset` are both empty when the reference carries no zone, so a caption bound to
either disappears rather than showing the reader's own zone back to them.

Only `time`, `date` and `zone-name` are drawable by every reader. **Anything else needs
`requiredComponentVersion: 2` and a fallback**, because negotiation catches an unknown node *type* and
never an unknown property *value*: a reader that predates one of these formats would draw an empty run
where a clock belongs. Ask for version 2 and carry a `time`/`date` run as the node's `fallback`, and such
a reader shows the locale-default clock instead of nothing.

```csharp
new UiDynamicText
{
    Key = "time",
    Value = reference,
    Format = UiTimeFormats.Time24Hour,
    RequiredComponentVersion = 2,
    Fallback = new UiDynamicText
    {
        Key = "timeLocalized",
        Value = reference,
        Format = UiTimeFormats.Time,
    },
}
```

### `macrodeck.clock-dial`

| Property | Meaning | Absent means |
|---|---|---|
| `value` | The time reference the hands are drawn from | Nothing is drawn |
| `seconds` | Whether the second hand is drawn | It is not |
| `color` | A literal `#rrggbb` for the dial's text-coloured marks | Every mark keeps the reader's theme |

`color` reaches every mark the dial would otherwise draw in one of the reader's text colours - both tick
weights, the hour hand and the minute hand - and stops there. The face, the second hand and the hub keep
the theme, so the moving hand still reads against a tinted face, and the major and minor ticks stay told
apart by the stroke and length they already differ by. It carries no version requirement: a reader that
predates it draws a themed dial, which beats a face that vanished into a fallback.

## Supported children

Neither carries children. Both are leaves.

## Events and interactions

Neither declares events. Both are read-only.

## Layout behaviour

`macrodeck.dynamic-text` sizes exactly like `ui.text`: its box is its font size, with a line height of one.
See [Text](/sdk/ui/components/text/) and [Sizing](/sdk/ui/concepts/sizing/).

`macrodeck.clock-dial`'s geometry is normative, for the reason `ui.range-bar`'s is: none of it follows from
the properties, and two readers that disagree on it draw visibly different clocks. Lengths are fractions of
`d`, the largest square that fits the element's box, centred in it - a dial in a box that is not square is
centred rather than stretched. A reader re-evaluates at least once a second and computes the hand angles
from the whole second; a sub-second sweep is deliberately excluded, since it is not derivable from the
reference and two readers each choosing their own interpolation would disagree. The fixtures in
`ui-model/fixtures/component-profile/` resolve the face, tick and hand geometry, and state the three hand
angles for one named instant so a renderer can check itself deterministically.

## Examples

```csharp
new UiDynamicText
{
    Key = "time",
    Value = UiValue.Of(UiTimeReference.InZone("America/New_York")),
    Format = UiTimeFormats.Time,
    Seconds = true,
    Size = 0.24,
    MinSize = 0.13,
    Weight = UiComponentTextWeights.Bold,
}
```

```csharp
new UiClockDial
{
    Key = "dial",
    Value = UiValue.Of(UiTimeReference.InZone("America/New_York")),
    Seconds = true,
}
```

Give both a `Fallback` for a reader too old to know either type - a `macrodeck.clock-dial` degrades to
`macrodeck.dynamic-text`, which in turn degrades to `ui.text`, so a reader that draws no dial but
understands a dynamic text still shows the right time.
