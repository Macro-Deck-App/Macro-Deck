# Component profile conformance fixture

The shared fixture every Macro Deck UI renderer is checked against. It exists because a canonical-JSON
tree on its own pins the **producer** - it says nothing about whether two renderers draw the same
picture from it.

Three artefacts, and the third is the one that does the work:

| file | pins |
|---|---|
| `conformance-tree.json` | the wire form: one `UiTree` in canonical JSON |
| `conformance-layout.json` | the **resolved geometry** at a stated basis, by node id |
| `conformance-clock-tree.json` | the wire form of the clock shapes - a reader-resolved time reference and the analogue dial |
| `conformance-clock-layout.json` | the dial's **normative geometry** resolved, plus the hand angles for one stated instant |
| `conformance-clock-formats-tree.json` | the wire form of the producer-pinned time and date formats, the run and dial colours, and a dynamic text asking for more than this profile ships |
| `conformance-clock-formats-layout.json` | those runs' sizes and colours resolved, and which node a reader draws when the version it is asked for is one it does not have |
| `conformance-slider-tree.json` | the wire form of the one interactive shape - the declared affordance, and the levels and colours around it |
| `conformance-slider-layout.json` | the slider's **normative geometry** resolved, including the interactive surface that is deliberately larger than the drawn track |
| `conformance-action-button-tree.json` | the wire form of the other interactive shape - the declared press names, the artwork framing, the ring and the fallback to `ui.stack` |
| `conformance-action-button-layout.json` | the button's **normative geometry** resolved: content box, artwork rect, ring and press feedback, plus the label |
| `conformance-picker-tree.json` | the wire form of the two dialog shapes - the line the user types into and the container that scrolls and asks for more, with the answer a row settles the dialog with |
| `conformance-music-player-tree.json` | the wire form of the two reader-resolved *progress* shapes - a position that keeps moving, the duration runs derived from it, and the artwork crossfade |
| `conformance-music-player-layout.json` | the progress geometry and runs resolved **at two instants**, plus the crossfade's own constants |
| `conformance-history-graph-tree.json` | the wire form of the layered shapes - a chart series behind two layers of content, and the reservation a live readout keeps |
| `conformance-history-graph-layout.json` | the chart's **normative geometry** resolved: the plot band, every point of a dense series, and which term of each length actually binds |
| `conformance-gauge-tree.json` | the wire form of the transformed shapes - a needle turned about a pivot below its centre, an identity transform, and two nested transforms |
| `conformance-gauge-layout.json` | the transform and pivot each `ui.transform` resolves to, and the box its children are drawn across |
| `conformance-building-blocks-tree.json` | the wire form of the building blocks - a grid placing a shape, an icon, a gauge, a toggle, a segmented control, a dial and a path, each with the fallback an older reader draws |
| `conformance-building-blocks-layout.json` | each grid cell resolved, plus the shape, glyph, arc, toggle track and segment face geometry at two bases |
| `conformance-modifier-tree.json` | the wire form of the universal modifiers - every `modifiers` member on ordinary nodes, the `ui.modifier` wrapper with every one of its properties, and the gesture event names |
| `conformance-modifier-layout.json` | what a reader writes for each of them: backgrounds, corners, the border drawn inside the edge, the accessibility attributes, the dim of a disabled region, and the wrapper's frame, padding, clip and mask resolved |
| this README | what the fixtures deliberately contain, so they are not trimmed by accident |

One tree per widget shape rather than one growing tree: the weather tree's proportions are themselves
part of what a renderer is checked against, and folding a second widget into it would dilute that.

`conformance-tree.json` is **hand-authored from the profile's rules**, never produced by running a
serializer and saving the output - that would only prove the implementation agrees with itself. It is
canonical JSON, so it is one line, like the wire goldens in
`ui-model/tests/MacroDeck.Ui.Model.Tests.UnitTests/Fixtures/`.

## What the tree deliberately contains

Trimming any of these turns the fixture into one a wrong renderer passes:

- across the trees, every primitive - including `ui.stack`, `ui.text`, `ui.image`,
  `ui.range-bar`, `macrodeck.dynamic-text`, `macrodeck.clock-dial`, `macrodeck.progress-bar`,
  `macrodeck.progress-text`, `ui.slider`, `ui.button`, `ui.layer`, `ui.chart`, `ui.transform`, `ui.shape`, `ui.icon`, `ui.grid`, `ui.gauge`, `ui.toggle`, `ui.segmented`, `ui.dial` - and every
  property key the profile ships; coverage is a property of the fixture set, not of any one file alone;
- a length whose `maxOfCross` **binds** (the forecast weekday, clamped by its row height) *and* one where
  `basis` binds (the range-bar thickness, in the same row) - a renderer that ignores `maxOfCross` passes a
  fixture containing only the second;
- a node whose `mainSize` and `size` differ by a factor of **1.9**, which a renderer conflating the column
  width with the font size cannot reproduce;
- all three text roles, so a renderer that hardcodes one colour fails;
- range bars covering a mid-track span with a marker, and a span ending exactly at `1` with **no `marker`
  property at all** - absent, not null;
- a day icon and a night icon as separate resources, so the day/night rule is visible in the fixture;
- a `UiResource` with all four members and one carrying only `resourceId`;
- a localization reference **with** arguments and one **without** - the argument map is ordinal-sorted and
  omitted when empty, and both spellings are part of the canonical bytes;
- an unknown type (`macrodeck.sparkline`) carrying `requiredComponentVersion` and a `fallback`, so
  degradation is exercised rather than assumed.

## What the modifier tree deliberately contains

- **every `modifiers` member** - `background` in all three shapes (hex, `linear`, `radial`), `radius`,
  `borderWidth`, `borderColor`, every `borderLine` value, `accessibilityLabel` both as a plain string and
  `accessibilityHint` as a localization reference, and `disabled`;
- a disabled `ui.button` that **still declares** `press` - a hand-written tree can say that, and the fixture
  states that a reader emits nothing inside a disabled region, dims the node once and marks it
  `aria-disabled`, while the button keeps its own corner because `radius` is absent;
- a labelled container and a labelled press claimant, which take `role` `group` and `button` respectively;
- **every `ui.modifier` property** - `padding`, `opacity`, all three `clip` values, `mask` in both shapes and
  `frame` with every one of its keys spread over three wrappers - including an `aspectRatio` that derives the
  side the parent leaves open and a `maxWidth` that binds before it does;
- a wrapper carrying its own `modifiers`, so the background covers the padding, and its opacity is the one
  value a reader writes, never multiplied into a second copy;
- every gesture event name (`drag`, `drag-end`, `swipe`, `pinch`, `pinch-end`) on one wrapper, which is also
  what keeps the platform from panning under it;
- an explicit `fallback` on that wrapper, so a reader without `ui.modifier` still shows the content.

## What the history-graph tree deliberately contains

- **three series lengths**: twelve points, three points, and **none at all** - the empty one is what
  separates a renderer that draws nothing from one that draws a flat line along the foot of the band,
  which would read as a real value of zero;
- a chart **with** `plotTop` and one **without** - absent means the band is the whole element, and a
  renderer that defaults it to anything else puts the series in the wrong place;
- a chart nested in a stack with a `mainSize`, so the band's own box is not the widget's box;
- a length whose `maxOfCell` **binds** (the title, held to 12.996 where the basis alone would give 26.4)
  *and* one where it does not (the subtitle, whose clamp sits far above the basis term) - a renderer that
  ignores `maxOfCell` passes a fixture containing only the second;
- a `digits` reservation that is **not a whole number**, so a renderer rounding it to digits loses the
  separator's width;
- `align: baseline` on a row of two runs at different sizes, which is the only alignment their boxes
  cannot fake;
- a `ui.layer` whose children are read in paint order, first furthest back.

## What the gauge tree deliberately contains

- a needle carrying **every** transform key at a non-default value - a `rotation`, a pivot below the centre
  (`originY: 0.875`), a `zoom` away from `1` and offsets of **opposite sign** - so a reader that applies the
  steps in a different order, or pivots zoom about the centre, resolves a different transform;
- a transform with **no keys at all**, which draws no transform whatsoever - absent, never a written `0` or
  `1` - so a reader that always writes an identity transform (and with it a new stacking context) fails;
- a transform **nested** in another, each resolving only its own keys, which is what lets them compose;
- a `fallback` on the needle that still shows the reading as text - a reader too old for `ui.transform`
  loses the needle, never the value.

The layout table states the same strings at both bases: offsets and the pivot are fractions of the element's
own box, so a transform is resolution-independent by construction.

## What the clock tree deliberately contains

- a time reference **with** a zone and one **without** - the zone-less spelling is `{"$time":{}}`, with
  the member omitted rather than written null, and both spellings are part of the canonical bytes;
- the three `format` values every reader can draw, including `zone-name`, which otherwise ships with no
  producer to conform to - the nine that have to be negotiated live in the formats tree below;
- `seconds` set on one dial and **absent** on another - absent, not `false`, for the same reason `marker`
  is absent on a range bar;
- a **two-step** fallback chain, `macrodeck.clock-dial` to `macrodeck.dynamic-text` to `ui.text`: the useful
  degradation is not the last one, because a reader that draws no dial but understands a dynamic text can
  still show the right time;
- a dial in a **non-square** box (`conformance.dialPlain`), which is what separates a renderer that fits
  the largest centred square from one that scales by the box - a square-only fixture passes both.

Like the weather tree, it is authored for the fixture rather than captured from a shipped widget view:
the point is to exercise the profile's rules, not to record what one producer happens to emit.

## What the clock formats tree deliberately contains

Held apart from the clock tree rather than folded into it: that tree's dial declares `fill`, so it takes
whatever space its siblings leave, and adding a node to it would move the very geometry it exists to pin.

- **every `format` value the profile ships past the first three** - the four `time-*` faces, the three
  ordered dates, `date-long` and `zone-offset`. A reader that quietly fell back to the locale's own shape
  passes a fixture carrying only `time` and `date`;
- both padded and unpadded spellings of **each** hour cycle, which is the pair that separates a reader
  honouring the format from one passing `hour: "numeric"` to its own formatter and letting the language
  answer;
- a run carrying `color` and runs carrying only `role`, so the override and the theme path are both drawn;
- a `macrodeck.clock-dial` with `color`, whose **second hand and hub stay on the accent** - a reader that
  tinted every mark passes a fixture that only checks the hands;
- a `macrodeck.dynamic-text` asking for `requiredComponentVersion: 3` - one past what this profile ships -
  beside its version 1 fallback. This is the degradation the pinned formats depend on: an unknown format
  value is caught by nothing, so without the version ask a reader draws an empty run rather than a clock.
  The fixture states that the asking node is **not** drawn and its fallback is.

## What the slider tree deliberately contains

The slider is the profile's only interactive element, so most of what this tree pins is what a reader is
allowed to *offer*, which no other fixture exercises:

- **three declaration shapes** - `events` of `["adjust","change"]`, `events` of `["change"]` alone, and no
  `events` key **at all** (absent, not an empty array). A tree carrying only the first passes a reader that
  ignores the declaration and offers interaction unconditionally; a tree carrying only the last passes one
  that never interacts. The rule is "send what the node declares", and it takes all three to check it;
- a `levelColor` **and** a slider with none - the second is the accent case, and a reader that resolved its
  own accent colour into the tree would pass a fixture containing only the first;
- `step` present on one slider and **absent** on another - absent, not `0`, for the reason `marker` is
  absent on a range bar. The two draw the same picture and land on different values;
- both axes, including the `vertical` one, whose fill runs bottom to top;
- a `level` of exactly `0`, which is where a reader tends to paint a rounded stub one radius wide instead
  of nothing;
- a one-step fallback to `ui.range-bar` carrying **both** colours - a reader too old to draw the
  slider still shows the right level and honestly cannot drag it;
- `background` on a stack and `color` on a text - the profile's two literal colours, which exist because a
  colour the *user* chose must not move with the reader's theme.

`conformance-slider-layout.json` resolves the track and thumb geometry at both bases and, for every
slider, states the **interactive surface separately from the drawn track**. Those two numbers differ on purpose: the
element's whole box takes the pointer while the pill is a fraction of it, and a renderer that closes the
gap draws the right picture and ships a control no thumb can hit. That is the one part of this primitive's
contract a screenshot comparison would never catch.

## What the action-button tree deliberately contains

The button is the profile's other interactive element, and unlike the slider it also carries its own
artwork and its own ring, so this tree pins three things no other fixture exercises:

- **four declaration shapes** - `events` declaring all four names, `events` of `["press"]` alone,
  `events` of `["press-end","press-start"]` alone, and no `events` key at all. A tree carrying only the
  pair passes a reader that infers `press` from `press-start`/`press-end` ending back to back, which is
  exactly the inference the contract forbids; a tree missing the undeclared case passes a reader that
  offers interaction unconditionally;
- a backdrop with **every** framing key at a non-default value - `fit: "cover"`, a `zoom` away from `1`,
  `offsetX`/`offsetY` non-zero and of **opposite sign**, and an `opacity` below `1` - next to a button
  that carries a `source` and **none** of those keys. The second is the case that matters: a reader that
  resolved its own defaults into the wire, or that treated absence as zero and drew a collapsed or
  invisible rect, passes a fixture containing only the first;
- a button with **no `source` at all** - a label-only face, distinct from a backdrop resolved to
  defaults, which is why both live in the tree side by side;
- a tinted ring (`borderStyle: "static"` with a `borderColor`), a `hue-shift` ring with **no**
  `borderColor` - the style that supplies its own colour and must not echo one back - and a button with
  **no `borderStyle`**, which is absence, never a `"static"` default and never the wire spelling `"off"`
  that this profile deliberately does not have;
- `background` present on one button and absent on another - absent means the reader's own accent
  colour on a button, unlike a stack, where absent paints nothing at all;
- text children spanning `wrap`'s three states - `true` alone, `true` together with `maxLines`, and the
  key left out entirely - plus one child with `fontFace` and one without, reusing the same properties
  `ui.text` already ships rather than inventing button-only ones;
- a `justify: "start"` + `align: "end"` layer next to a centred one, so a renderer that conflates the
  button's own main and cross axis cannot reproduce both from one fixture;
- a **one-step** fallback to `ui.stack` on the fully-declared button, carrying the same layout,
  background and children and none of the button-only properties - a reader too old for `ui.button`
  still shows the right face and honestly cannot be pressed.

## What the music-player tree deliberately contains

The music player is the widget that made the profile grow a second reader-resolved reference (ADR 0064),
so this tree pins the four things a renderer can get wrong about a position that keeps moving, one about
artwork that changes, and one about the box that artwork fills:

- **`rate` absent and `rate: 0` side by side.** Absent means normal speed - the case whose display
  changes every second, and the one kept key-free for exactly that reason - while a halted medium writes
  `0`. A tree carrying only one of them passes a reader that ignores `rate` entirely, which would leave a
  paused track counting up forever or a playing one frozen;
- **a reference with `durationMs` and one with none at all** (`conformance.stream`, a live stream). The
  second is the case a reader that divided by a missing length would fail: there is no fraction to draw,
  so the track stays empty and `remaining`/`duration` resolve to nothing;
- **a `macrodeck.progress-bar` with no `value` at all** (`conformance.loading.progress`) - absent, not a
  zero-position reference. It draws the same empty track and is what a widget still waiting for its first
  read shows;
- **all three duration formats**, each on its own node, and each stated in the layout table at two
  instants - the anchor and thirty seconds after it. That pair is the whole point: a reader that draws the
  anchor and stops reproduces the first column and fails the second;
- **both progress colour cases** - a bar carrying `startColor`/`endColor` and one carrying neither, which
  is the accent case a reader that resolved its own accent into the tree would pass without;
- **`transition: "crossfade"` present on an image and on a button's artwork, and absent on a third
  image** (`conformance.playing.badge`). Absence is not a slower crossfade: the source is replaced
  immediately, which is also what a reader that never heard of the key does;
- **`brightness` and `saturation` on the paused cover, and on nothing else.** They are what a paused
  album cover is drawn with, and the layout table states the luma coefficients: two readers using
  different ones desaturate the same cover to visibly different greys. Every other image carries
  neither key, so a reader that resolved an identity filter into every image fails the second half;
- **an events-less `ui.button` carrying a `source`** - artwork behind children, with no affordance at
  all. The action-button tree has an events-less button and a button with artwork, but never both in one
  node, and this is the shape the Music Player's full cover style actually ships;
- **`corner: "tile"` on that same button, and on no other button in any tree.** It is the one nested
  button in the fixtures drawn to the edges of the tile, so it is the one whose corner is the tile's
  rather than a fraction of its own height - and every button in the action-button tree, which pins that
  fraction by example, deliberately carries no `corner` at all;
- **both fallback chains**: `macrodeck.progress-bar` to a `ui.range-bar` frozen at the anchor, and
  `macrodeck.progress-text` to a `ui.text` holding the run the producer composed for that moment. Both
  are honest - the wrong second, never the wrong picture.

Every reference in the tree is anchored at the same instant, so the layout table can state one
resolution for all of them.

`conformance-action-button-layout.json` resolves the content box, the artwork rect and the ring at both
bases, and states the press feedback's constants once rather than per node, since they are durations and
an alpha, not lengths. `ringWidth` is the one length in this table that does **not** halve between the two
bases - `2.0` at both - which is the profile's deliberate exception to the halving rule everything else in
this file follows.

## The layout table

`conformance-layout.json`, `conformance-clock-layout.json`, `conformance-clock-formats-layout.json`,
`conformance-slider-layout.json`,
`conformance-action-button-layout.json` and `conformance-music-player-layout.json` give the resolved
geometry in device-independent units for two tile
sizes, 240 and 120. Every value at 120 is exactly half its counterpart at 240 - `ringWidth` in the button
table stated as the one deliberate exception - which is the fixture's second job: it demonstrates that a
widget tree is resolution-independent, so a resize is a client-side relayout and never a host round-trip.

`conformance-clock-layout.json` additionally resolves the dial's normative geometry - face, tick and hand
radii and stroke widths, and the hub - as multiples of the dial edge, and states the three hand angles for
one named instant so a renderer can reproduce them deterministically. Without that, the geometry paragraph
on `macrodeck.clock-dial` would be an assertion no test can check.

A renderer claims component-profile support by reproducing these tables within its own rounding tolerance.

## Where each side is checked

| side | what asserts it |
|---|---|
| the wire form | `MacroDeck.Ui.Tests.UnitTests`' `ComponentProfileConformanceFixtureTests` - every tree is canonical, the set covers the whole vocabulary, and the trees still carry both length branches, both reference spellings, the slider's affordance shapes, the button's declaration shapes and the degradation paths |
| the Angular renderer | `ui-widget-node.component.spec.ts` - resolves the same lengths this table lists, in both the cross-clamped and basis-bound branches, and asserts the 1.9 ratio; `widget-slider-interaction.spec.ts` covers the affordance and the drag; `widget-button-interaction.spec.ts` covers the press ordering and the artwork/ring painting; `widget-progress.spec.ts` covers the position carried forward, the three runs and the crossfade |
| a future renderer | this table, reproduced in its own test suite |

The Angular assertions restate the table's numbers rather than reading this file: the fixture lives outside
the Angular workspace, and CI invokes `ng test` directly rather than through an npm script, so a copy step
would silently not run there. If the two ever disagree, this file is the contract and the renderer is
wrong.

## What the building-blocks tree deliberately contains

- a `ui.grid` with a declared `rows`, a child spanning two columns and one spanning two rows, so the dense
  row-major placement is visible in the cell geometry rather than assumed;
- every new type with the fallback the issue recommends: a stack with the same background, a text label, a
  range bar at the same level, a button, a stack of buttons, and a slider keeping the dial's own events;
- a decorative path shape with **no fallback at all**, because drawing nothing is an allowed degradation;
- a toggle that is on and a segmented control with a selection, so the painted state is checked, not only
  the track;
- a full-turn dial (`0` to `360`), the case the seam rule exists for.
