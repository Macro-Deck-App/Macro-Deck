# ADR 0102: First-fit layouts are chosen by measured text

Status: Accepted

## Context

A plugin cannot know whether a text fits: it is laid out on the viewing device in that device's font, size and
language. A caption that sits beside a name when there is room and under it when there is not cannot be decided
by the view, and `ui.responsive` ([ADR 0094](0094-responsive-layouts-are-chosen-by-the-reader.md)) chooses by
the box alone. The renderer already measures text to shrink it to its minimum size.

## Decision

**The reader chooses the first layout whose texts fit.** `ui.first-fit` carries its layouts as children in
order of preference and has no properties. A layout fits when no text in it is truncated and it does not
overflow the box the node is given; the last child is drawn when none fits and by any reader that cannot
measure.

**Every layout is painted and measured.** All children are laid out in the same box, the chosen one visible and
the others hidden from sight, pointers and assistive technology. The choice is a read of the settled layout, so
it follows the box, the text, the font and the language without rebuilding anything, and per-client state in a
layout survives a switch.

**The box never depends on the choice.** The node is sized by its own slot, or by its last child where it has
none, so the choice cannot change the box it is made against.

**Older readers get the last layout.** A reader that does not know the type draws the fallback; with none set
the last child is emitted again under `<id>._fallback`, as ADR 0094 does for the default.

**Tile-level claims count every layout.** Only the renderer knows which layout fits, so the host and the
reader look for a control in all of them.

## Consequences

- Every layout counts toward the tree limits and is painted, so tickers and images inside one are repeated per
  layout; the docs ask authors to keep layouts small and controls identical.
- A tree root that is a `ui.first-fit` gets no tile transparency or press ring.
- A threshold or a measurement option can be added later as an additive property.
- A client with its own renderer draws the fallback until it implements the measurement.

## References

- [First fit](../../docs/src/content/docs/ui/components/first-fit.md)
