---
title: Text
description: ui.text is a single run of text, sized as a fraction of the view basis and laid out with a line height of exactly one.
---

`ui.text`

## Purpose

`ui.text` draws one run of text. Its box is exactly its font size - a reader lays it out with a line
height of one, so a stack's gaps are the only vertical spacing in a view and two readers agree on where a
run sits.

## Properties

| Property | Meaning | Absent means |
|---|---|---|
| `text` | The content - a literal string or a localization reference, resolved in each reader's own active language | Nothing is drawn |
| `size` | The font size, a length | The size is left to the reader |
| `minSize` | The floor `size` may shrink to so the run fits its box, a length | The run never shrinks; it ellipsizes instead |
| `weight` | The font weight - `regular`, `medium`, `semibold`, `bold` | `regular` |
| `role` | The semantic colour - `primary`, `secondary`, `muted` | `primary`. Ignored when `color` is present |
| `color` | A literal colour, as `#rrggbb`, overriding `role` | `role` decides |
| `align` | Alignment within the run's own box - `start`, `center`, `end`, `stretch`, `baseline` | `start` |
| `maxLines` | How many lines the run may occupy before it ellipsizes | One |
| `wrap` | Whether the run may break across lines at all | The run stays on one line and ellipsizes - see below |
| `fontFace` | A typeface identifier from Macro Deck's own font catalogue | The reader's own default face |
| `digits` | How many digit widths the run reserves | The run is exactly as wide as its content |

`role` is a theme role rather than a literal colour, and `color` is the escape hatch for a colour that is
data rather than theme - see [Colours and text](/sdk/ui/concepts/theming/) for the full split and for why
a reader rejects any other spelling of either.

`wrap` defaults to the restrictive reading, not the permissive one: a reader that has never heard of the
key still keeps a run inside its box, rather than letting it swallow whatever sits below it.

`fontFace` degrades by holding text back rather than by drawing it wrong. A reader holds the run until the
named face is usable and then reveals it, instead of drawing it in a fallback face and swapping - and if
the face never arrives, it reveals the run in its own default face. A run that never appears is worse than
one in the wrong font.

`digits` exists for a live numeric readout: a value that gains or loses a digit as it changes otherwise
drags whatever sits beside it sideways once a second. It is a count of digit widths, not a length, because
how wide a digit is belongs to the reader's own face and size; it is fractional because a decimal separator
takes materially less room than a digit. A reserved run is drawn with every digit on the same advance
width, so it does not shift as it counts, and its content is centred in the reservation when the content is
narrower than the space reserved for it.

## Supported children

None. `ui.text` is a leaf.

## Events and interactions

`ui.text` declares no events. It is never interactive - a value shown by a text is patched by its
producer, never entered by the reader.

## Layout behaviour

A text's cross-axis extent is fixed at its line height - exactly its resolved `size` - regardless of what
its parent stack offers. Its main-axis extent follows the ordinary rule every leaf follows: `mainSize` or
`fill` if declared, otherwise the reader's own best measurement.

That measurement, for a text with neither `mainSize` nor `fill`, is the line height across the stack and
**nothing at all along the main axis** - a text does not know its own text length without a font, which a
layout pass run without one cannot assume. Give every text in a row its own `mainSize` whenever a sibling
fills that row, or the filling sibling is handed the width those texts need on top of its own, and pushes
them out of the box. See [Sizing](/sdk/ui/concepts/sizing/) for the full length model.

## Example

```csharp
new UiTextRun
{
    Key = "temperature",
    Text = AppStrings.Widgets.Weather.TemperatureValue(23),
    Size = 0.17,                                   // 0.17 x basis
    MainSize = UiSize.FromBasis(0.144, 1.2),        // min(0.144 x basis, 1.2 x row height)
    Weight = UiComponentTextWeights.Bold,
    Digits = 2.5,
}
```

`size` (the font) and `mainSize` (the extent along the parent's main axis) are different properties -
setting one does not imply the other.
