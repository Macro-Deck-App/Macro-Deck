---
title: First fit
description: Offer several layouts for the same content and let the reader draw the first one whose text fits, measured in the viewer's own font.
---

`UiFirstFit` holds several layouts for one place in the tree, in order of preference. The reader draws the
first one whose texts fit the box without being cut off or overflowing, and the last one when none does. It
measures with the font it paints in, so you do not have to guess the viewer's font, size or language.

`ui.first-fit` (component version 1)

## Example

A list row with a name and a caption. When both fit on one line they sit side by side; when they do not, the
caption moves under the name instead of both being cut off.

```csharp
new UiFirstFit
{
    Key = "namegroup",
    Children =
    [
        new UiStack
        {
            Key = "inline",
            Direction = UiComponentDirections.Horizontal,
            Children = [name, caption],
        },
        new UiStack { Key = "stacked", Children = [name, caption] },
    ],
}
```

## What fits means

A layout fits when, in the box the node is given:

- no text in it is cut off. A single-line text counts as cut off when its natural width is wider than the
  room it has, after any `MinSize` shrinking; a wrapping or line-limited text when it needs more lines than it
  is allowed;
- the layout itself does not overflow the box. A text may reach slightly past its parent, as it always may so
  descenders are not clipped; overflow beyond that makes a layout not fit, and a rounding difference of half a
  pixel in the box does not. A single-line text has no such allowance: it is cut off as soon as it needs an
  ellipsis.

A text that shrinks to its `MinSize` to fit counts as fitting. Images, shapes and other non-text content are
not measured.

The reader decides again whenever the box, a text, the font or the language changes.

## The box it chooses by

The node's box comes from its own sizing, never from the layout it draws. As the root of a widget it gets the
whole tile. Inside a stack give it `Fill` or `MainSize` like any other child, or put it in a stack whose
cross axis is definite. A node that has to size itself takes the size of its last layout, so a node without a
definite box is judged against the room the last layout needs.

`MainSize`, `Fill`, `ColumnSpan` and `RowSpan` go on the `UiFirstFit`. A layout that sets them is rejected
when the view is built, and so is a layout that is a `UiWhen`, a `UiRepeat` or a fragment.

## What every layout costs

Every layout is built, kept current, sent to the reader and painted, whichever one is on screen. They all
count toward the tree limits: 2000 nodes, 192 KiB per tree and 64 KiB per patch. A clock, an image or an
input inside a layout exists once per layout, and the copy an older reader draws (below) is one more.

Keep the layouts small and build them from a helper method. Keep any control identical across layouts, so a
press means the same thing whichever layout is drawn.

## Older readers and readers that cannot measure

A reader that does not know `ui.first-fit` draws the node's `Fallback`. When you set none, the last layout is
sent a second time as the fallback, under ids below `<id>._fallback`. A reader that cannot measure draws the
last layout as well, so make the last layout the one that is always acceptable, even if it is not the one you
prefer. The same consequences as for [Responsive](/ui/components/responsive/#older-readers) apply: the copy is
patched along with the original, the key `_fallback` is reserved, the copy's ids are longer, and a
[configuration input](/ui/views/configuration/) cannot sit inside the last layout unless you set an explicit
`Fallback`.

## Presses and hardware keys

A pointer press on a drawn control always reaches that control. For the tile's own press, a keyboard
activation or a hardware key on a device, the reader and the host look for a control in every layout, because
only the renderer knows which one fits. A `UiResponsive` nested in a layout counts its default layout there.
When the root of the widget is a `UiFirstFit`, the tile does not treat it as a stack or button: it gets no
transparent tile background and no tile press ring, as with a `UiLayer` root.

## Testing

`UiTestHost` cannot measure, so it lists every layout. `ByType`, `SingleByType` and `ByText` see all of them,
not only the one a reader would draw, and never the copy an older reader draws.

## Reference

| Property (`UiFirstFit`) | Wire | Meaning |
|---|---|---|
| `Children` | `children` | The layouts in order of preference; the last is the fallback |
| `MainSize`, `Fill`, `ColumnSpan`, `RowSpan` | as on any node | The node's slot in its parent |
| `Fallback` | `fallback` | Your own fallback; when absent, a copy of the last layout |

Reader rules:

- Paint every child across the whole box. Show the first one whose texts are not cut off and which does not
  overflow the box, else the last. Hide the others from sight, pointers and assistive technology.
- Measure after the texts have settled, and choose again when the box or a text changes.
- Size an unsized node like its last child.

## See also

- [Responsive](/ui/components/responsive/) - layouts chosen by the size of the box
- [Text](/ui/components/text/)
- [Stack and layer](/ui/components/stack-and-layer/)
- [ADR 0102](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0102-first-fit-layouts-are-chosen-by-measured-text.md)
