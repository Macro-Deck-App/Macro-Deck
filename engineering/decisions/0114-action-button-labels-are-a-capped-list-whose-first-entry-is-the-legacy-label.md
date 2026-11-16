# ADR 0114: Action Button labels are a capped list whose first entry is the legacy label

Status: Accepted

## Context

An Action Button had one label, stored as flat keys on the widget data (`label`, `labelColor`,
`fontSize`, `textAlign`, `labelPosition` and the styling keys) and overridable per state. Plugins change
it through `WidgetAppearancePatch`, device providers draw it from `DeviceSurfaceAppearance`, and both
are published contracts a plugin may stay compiled against indefinitely.

Two requests needed more: moving a label away from its anchor, and showing several labels on one
button. Building them separately would have produced two label models that persistence, the editor, the
text pipeline and the device contract each had to carry.

## Decision

- A button has at most four labels. The first is the primary label and stays exactly where it was: the
  existing flat keys, the existing state, root, default cascade, the existing patch and device fields.
  Stored buttons and Macro Deck 2 imports are a list of one without being rewritten.
- The other labels are an `extraLabels` array of objects that reuse the primary label's key names. An
  extra label takes defaults for what it leaves unset, never the primary label's values.
- A label's position is `labelX` and `labelY`, each 0 to 100 percent of the free space in the button's
  content box: 0 is flush start, 50 centred, 100 flush end. The existing `textAlign` and `labelPosition`
  values are the presets 0, 50 and 100 and are read as such wherever no free position is stored, so
  stored buttons keep their place. The tree composes a position from `ui.layer`, two nested
  `ui.transform` nodes and `ui.stack`; the UI model gains no positioning primitive. A button whose
  labels all sit on presets and has no extra label keeps the single-label tree.
- Each state owns its extra labels. The button's own list shows only while state mode is off and is
  never a fallback for a state.
- Labels are addressed by number, 1 to 4, in actions and in `WidgetAppearancePatch.LabelNumber`. An
  absent number means the primary label.
- Every addition to the SDK and the protocol is optional. A button without extra labels whose label
  sits on a preset produces the tree it produced before, and its device payload differs only by the
  optional position fields once the button was saved again.

## Consequences

- A host that predates `LabelNumber` applies a numbered patch to the primary label, and a plugin cannot
  detect that. The public documentation states it; a way to discover label support would be a separate
  additive SDK change.
- A device provider or UI client that predates the list draws the primary label only. A provider that
  predates free positions draws it at the third of the button `TextAlign` and `LabelPosition` name,
  which the host derives from the position.
- A patch's `TextAlign` or `LabelPosition` is a preset: it replaces a free position on its axis.
- Turning state mode on hides the button's own extra labels until it is turned off again, because states
  do not fall back to them.
- Addressing by number means removing a label renumbers the ones after it, so a flow that targets
  label 3 follows the position, not the label it was written for.
- Raising the cap later is additive; lowering it, or changing what the first entry means, breaks stored
  data and both contracts.

## References

- [Button labels](../../docs/src/content/docs/features/button-labels.md)
- [ADR 0026](0026-plugin-protocol-and-sdk-boundary.md), [ADR 0056](0056-widget-state-is-addressed-by-stable-state-id.md)
