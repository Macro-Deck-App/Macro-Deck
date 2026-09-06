# ADR 0065: The component profile's authoring contracts

Status: Accepted

## Context

A widget view differs from a configuration view in three ways that matter: it is drawn at whatever size
its grid cell happens to be, on clients whose densities have nothing in common; it is long-lived and
glanceable rather than transient; and it shows artwork.

[ADR 0064](0064-components-are-a-registry-over-two-namespaces.md) settles how a component type is
registered and named. This one settles what a producer may put in a tree and what a reader owes when it
draws one - the questions each built-in widget raised as it was migrated onto the profile.

## Decision

### Geometry, colour and text are contracts, not conventions

**Lengths are fractions of the widget basis, never pixels.** A length is a fraction of the smaller side
of the content box, optionally clamped against the containing stack's cross extent, so one tree is
correct at every size and a resize is a client-side relayout rather than a host round-trip.

**Theme colours travel as roles; data-derived colours travel literally.** Text carries
`primary`/`secondary`/`muted`, resolved by each renderer against its own live theme, so a theme change is
a repaint with no refetch and no new tree. A value-encoding colour — a temperature ramp — travels as hex,
because what 30 °C looks like must not change with the theme. **A colour a person chose is on the data
side** for the same reason. Absence stays meaningful and differs per component: an absent stack
background paints nothing, an absent button background is the reader's own accent, and an absent slider
level colour is the reader's accent — which is why a producer must never resolve an accent to hex itself.

**Resources travel as host-served handles.** A node references bytes by handle and the host serves them
with a strong entity tag equal to the content hash, so a tree never carries bytes and an icon shown by a
hundred widgets costs one handle per node and one transfer per client.

**Text is never resolved by the producer.** Every user-visible string is a localized reference resolved
per client, so a language change is a client-side re-render with no session churn and no request to
whatever supplies the widget's data.

### Reader-resolved content earns a type, not a property

This is the profile's central rule. A reader that does not know a *type* is caught by capability
negotiation and draws the node's fallback; a reader that does not know a *property* ignores it and draws
something wrong with no signal that it did. Degradation has to be visible to negotiation.

- **Time.** A tree carries `{"$time":{"zone":…}}` — "now, in this zone, on the reader's synchronised
  clock" — never an instant. The tree is static while the display is not, so a ticking widget costs no
  patch and keeps advancing while the connection is down. It lives in the model beside the resource
  handle rather than inside the localization contract, which every plugin compiles against.
- **Media progress.** `{"$progress":{positionMs, durationMs, rate, anchor}}` means "the medium was at
  this position at this anchor and has advanced at this rate since", resolved on the same synchronised
  clock. Absence carries meaning and the common case costs no key: absent `rate` is ordinary playback, a
  halted medium writes `0` so "paused" is a value rather than an inference, and absent `durationMs` means
  unknown — a live stream has a position and no end. The anchor has exactly one spelling, UTC to the
  millisecond, so two producers meaning the same moment cannot give one tree two canonical encodings.
- **Reference and format are separate.** The reference says which instant; the consuming node says how to
  draw it. That is what lets a digital run and a dial share one value shape.
- **A format is a closed list with stated results, not a format string.** Handing a reader a CLDR pattern
  would make the contract the union of every platform's formatter. Each value names what the run must end
  up showing, which a browser and a mobile runtime can both satisfy with their own formatter.

**The reader's language decides the shape of a time, so a producer never composes one.** Hour cycle,
separator, digit system, writing direction and the position of the day period all come from the reader's
locale. Composing hour, seconds and day period as three nodes renders `9:40:32오후` in Korean, loses the
separating space in English, and reverses in right-to-left languages. What a producer *may* pin — a
12-hour face, an unpadded hour, a date's field order — is a further set of whole-result format values,
because a deck is a fixed display its owner arranges rather than a document each reader localises. A new
format value is **negotiated**, not merely sent: negotiation catches an unknown type and never an unknown
property *value*, so a reader predating one would fall through its switch and draw an empty run. A node
using one therefore declares a higher required component version and carries a locale-default fallback.
Colour is the opposite case and is deliberately not negotiated: dropping a whole node to a fallback over
a colour would trade a right clock in the wrong colour for no clock at all.

### Interaction is declaration-driven

A node's `events` property lists the names its producer accepts, and a reader offers exactly those and
nothing else. There is deliberately **no `disabled` property**: a second source of truth for "may the
user touch this" is one that can contradict the first. An unbound slider declares nothing and is drawn as
a level that cannot be moved. A component definition's own event list is metadata and never a gate —
gating there would either let a component emit interactions the producer never opted into, or silence a
node whose definition forgot to list an event.

**An interactive level travels as a fraction of its track, never in the producer's units.** A reader
snaps and paints locally so the control never waits for a round trip, and a producer's units — a track
duration, a decibel — never reach a renderer along with the job of formatting them. The number a person
reads is an ordinary text node the producer patches, which keeps it localized.

**The reader paints its own level for the duration of an interaction and ignores the producer's.** A
widget session is shared and polled; without this rule the producer's one-second-old value fights the
finger. The producer reconciles afterwards, holding the value until the integration confirms it or a
short timeout passes — on behalf of the clients that are *not* dragging as well.

**A producer that must not act on intermediate values does so by not acting, not by refusing the event.**
A seek must not be called per move but its readout must still follow the finger, so a bound slider
declares both an adjust and a change event and the producer decides which to push.

**Press names are separate, not one name with a phase payload.** A completed press, a held press and the
two boundaries are four names, because the failure mode of the unimplemented half has to be the harmless
one: a reader implementing only the completed press runs the button's main flow correctly, where one
implementing only a boundary would leave the press doing nothing. Four names also keep the declaration
meaningful — a button with a long-press flow must be distinguishable from one without, or every reader
arms a timer and sends an event the host discards. **The declared event set replaces the client's own
guess** at whether a press can advance a button's state: what a press does is the host's answer, stated
once in the tree it serves.

**Press feedback is painted by the reader, immediately, and is normative** — the tint, its timings and
its visibility floor are written on the primitive. A reader never waits for the producer: the round trip
runs the flow, it does not confirm the touch.

### Where properties are the right shape

A property is right exactly when a reader that ignores it draws something correct but plainer. Artwork
transitions are a property, so a reader that has never heard of the key replaces the artwork immediately.
Artwork adjustment is `brightness` and `saturation` rather than one combined "dim", because two
multipliers say what they do and each renderer maps them onto its own image filter; they are deliberately
not `opacity`, which lets the ground show through, and the saturation result is normative down to its
luma coefficients so two readers do not desaturate the same cover to different greys.

A button's artwork is a **property of the button, not an image child**: a stack cannot express "this
child fills the box and the others sit over it", and the alternatives put a second layout model into the
profile for one component. It also keeps the tree's *shape* fixed while the face changes, so a state flip
is a property patch rather than a structural reconcile.

A font face travels as an **identifier, not a resource**. Faces are host-owned, and measurement settled
the rest: 46 of the 374 faces installed on one development machine exceed the resource byte limit, the
largest by two orders of magnitude. That limit budgets resident memory and per-client transfer of
*plugin-supplied* bytes; system fonts are neither.

### The corner radius is part of the surface

A widget tile is a rounded rectangle, the corner eats into the box from outside, and nothing inside the
tree could see it — so every widget carried a hand-picked padding of its own, none derived from the
radius it was clearing. The surface therefore carries `cornerRadius` in the profile's reference
coordinate space.

It is admitted **because it is not a size**. A widget spanning four cells is drawn with the same corner
as one spanning one, so the guard it might have broken — a view's tree must not depend on how large it is
drawn, or a resize would rebuild it — is untouched. Those guards are narrowed rather than removed: a view
still may not take a width, a height or a basis, and the surface still may not carry the widget's span or
pixel size. A radius that changes rebuilds the tree through the same session invalidation a
configuration change already goes through. The inset is derived rather than chosen — a corner of radius
`r` intrudes `r(1 − 1/√2)` on its diagonal — floored at the gap between two tiles, because clearing the
curve turned out not to be the same as being readable.

## Consequences

- A renderer claiming component support owes the normative geometry documented on each type, a
  synchronised host clock, a locale-aware formatter and a position it carries forward itself. The
  conformance fixtures exist so those are checkable rather than asserted — the media fixture states the
  resolved position at the anchor *and* thirty seconds later, which is what separates a reader that
  advances from one that draws the anchor and stops.
- A reader too old for an interactive component draws its fallback: a slider degrades to a range bar at
  the same level, honestly not draggable; a button degrades to a stack with the same layout, background
  and label, without the artwork or the ring. The right picture, minus what the older vocabulary cannot
  say.
- Snapping states that **a tie rounds up**, because the obvious rounding primitive differs by platform —
  .NET rounds half to even, JavaScript rounds half up — and a reader that disagreed would paint one grid
  point while the producer acted on its neighbour.
- The normative press geometry states the element's whole box takes the pointer while the drawn track is
  a fraction of it, so a renderer closing that gap draws the right picture and ships a control no thumb
  can hit.
- The host re-anchors a media reference only when the position it reads has drifted past a threshold from
  the one already predicted, so a track playing undisturbed emits no patch at all between track changes.
  Two readers may still differ *within* a second, since sub-second interpolation is neither required nor
  forbidden.
- A clock dial is honestly a clock-specific renderer, specified once instead of reimplemented per client:
  every renderer still implements a dial, but it implements one written contract rather than inventing
  one. Sub-second hand interpolation is excluded so two readers agree.
- Displayed times changed for users in twelve-hour languages, who previously saw a hard-coded 24-hour
  clock, and every reader now uses the synchronised host clock rather than the device clock.
- Every icon on every deck is a registered UI resource. That store is bounded and evicts on icon update
  and delete; warming those handles the way icon URLs are warmed is not done and remains open.
- A legacy image URL holding a data URI is decoded into a resource; one holding an `http(s)` URL is
  ignored, because a widget tree must not be able to make the host fetch a stored URL on every session
  open.
- Widget sessions are per device: the session registry rejects a principal mismatch and admin scope does
  not bypass it, so the synthetic provider id carries the principal — which turns the per-provider session
  cap into a per-device cap rather than a ceiling on how many devices may show one widget.

## References

- [Issue #744](https://github.com/Macro-Deck-App/Macro-Deck/issues/744) and its children
  ([#746](https://github.com/Macro-Deck-App/Macro-Deck/issues/746),
  [#747](https://github.com/Macro-Deck-App/Macro-Deck/issues/747),
  [#748](https://github.com/Macro-Deck-App/Macro-Deck/issues/748),
  [#749](https://github.com/Macro-Deck-App/Macro-Deck/issues/749)),
  [Issue #401](https://github.com/Macro-Deck-App/Macro-Deck/issues/401),
  [Issue #828](https://github.com/Macro-Deck-App/Macro-Deck/issues/828)
- [ADR 0064](0064-components-are-a-registry-over-two-namespaces.md)
- [`ui-model/fixtures/component-profile/README.md`](../../ui-model/fixtures/component-profile/README.md)
