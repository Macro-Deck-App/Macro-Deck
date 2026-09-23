# ADR 0094: Responsive layouts are chosen by the reader

Status: Accepted

## Context

A component tree scales with its box ([ADR 0065](0065-the-component-profile-authoring-contracts.md)):
every length is a fraction of the basis, so one tree is correct at any size and a resize costs no
round-trip. It cannot change its structure. A widget at 2x1 wants its icon beside the text rather than
above it, a folder view on a phone wants less than on a tablet, and a dialog wants a narrower layout on a
narrow screen.

ADR 0065 keeps the size away from the view on purpose: a view may not take a width, a height or a basis,
and the surface does not carry a widget's span, because a tree that depended on its size would be rebuilt
on every resize. Only the reader knows the box, and it knows it continuously.

## Decision

**The reader chooses among layouts the view has already declared.** `ui.responsive` carries a default
layout and ordered conditional layouts as its children, with one condition object per conditional
layout in its `variants` property. The reader measures the node's own box, draws the first layout whose
condition holds, else the default, and chooses again whenever the box changes. The view still never
receives a size, so the guard in ADR 0065 stands.

**Conditions are bounds in cells and aspects.** Widths and heights are in cells of 120 reference units,
the unit `UiLength.Cell` already defines, so a deck tile's span maps onto them exactly. Outside a deck,
a cell is 120 of the reader's own layout units. Minimums include their value, maximums exclude it, and
both compare with a fixed tolerance so scaled pixels cannot flip a layout at an exact cell boundary. A
bound on a side the reader does not know never holds.

**One rule, published once and implemented twice.** `UiResponsiveSelection` in `MacroDeck.Ui` is the
rule over the wire form; the TypeScript runtime implements the same rule, and a shared conformance
fixture pins both. The host needs it too: a hardware key on a device is claimed by the control a tile
shows, and the host computes that tile's box from its span and the folder's spacing, as a client lays it
out.

**Tile-level claims follow the drawn layout only where the tile's box reaches.** A `ui.responsive` at
the root, or reached from it through nodes that hand on their whole box, is resolved with the tile's
box when deciding whether the tile's own press belongs to a control. Anywhere deeper, the claim counts
the default layout, because nothing outside the renderer knows that node's box.

**Older readers get the default layout without the author asking.** A node the reader does not know
draws its fallback; when the author sets none, the default layout is emitted again as the fallback,
under ids below `<id>._fallback`. This departs from `ui.modifier`, which invents no fallback, because a
responsive node without one would leave an older reader with nothing where the whole widget should be.

## Consequences

- Every layout is materialized and patched whichever one is shown, and the default is sent twice unless
  the author sets a fallback. Trees with many or large layouts reach the protocol's size limits sooner.
- Per-client state held by the reader, such as a text field's unsent text, is lost when it switches
  layouts.
- An older reader walks every layout when deciding the tile's own press, since it cannot know which one
  the new reader would draw.
- The configuration profile is out of scope. Its stacks wrap already, and the widget editor's shell
  adapts to the window by itself.

## References

- [Responsive](../../docs/src/content/docs/ui/components/responsive.md)
- [ADR 0065 - The component profile's authoring contracts](0065-the-component-profile-authoring-contracts.md)
