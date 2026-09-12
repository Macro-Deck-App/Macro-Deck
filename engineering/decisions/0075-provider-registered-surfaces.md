# ADR 0075: Widget types, folder views and modals are provider-registered and served through one provider

Status: Accepted

## Context

Three surfaces arrived at the same problem in turn.

**Widget types were a closed enum** in the host domain, so only six could exist and only Macro Deck could
add one. Once the last built-in widget became a host-built tree drawn by a generic renderer, nothing
about drawing a widget was specific to those six any more — the enum was the only thing left saying there
were six ([#750](https://github.com/Macro-Deck-App/Macro-Deck/issues/750)). Opening the set was not
enough on its own: the registry had no owner, presentation lived in a hardcoded list in the Angular app,
and only an in-process provider could draw a widget, because resolution went through a synthetic provider
id that the remote registry — which keys on a real plugin id — can never answer
([#843](https://github.com/Macro-Deck-App/Macro-Deck/issues/843)).

**Folders were tied to the widget grid.** A folder should be renderable entirely by an integration — a
Home Assistant dashboard, an OBS mixer — with the grid becoming one implementation among several rather
than an intrinsic property ([#785](https://github.com/Macro-Deck-App/Macro-Deck/issues/785)).

**An action could only read stored configuration**, never start an interaction. Opening a modal and
awaiting a typed result runs into three facts: every host callback carries a 30-second request timeout,
which a person answering a dialog is not bound by; a flow that has not finished within seconds detaches
and keeps running under a 10-minute cap with a limited number of concurrent runs; and a UI session's
owning principal is what keeps one device's half-filled dialog off another device's screen, while an
action carries a client id, which is not a principal.

## Decision

**A provider registers a descriptor to be *listed*, and serves the surface through its existing
`IUiProvider` to be *drawn*.** Two contracts, not one, in every case: a provider answers nothing to be
listed, and a session opens only for something somebody is looking at.

### Widget types

A widget type is an **open string id**. The built-in spellings are frozen at their old enum member names,
so nothing on the wire, in a profile file or in a plugin's type matching changes — widgets are not
database rows, and the JSON store already serialised those enums by name. A registry answers every
question about a type and owns the two-spelling tolerance clients have always had, so a client's
`action-button` and a stored `ActionButton` reach one type in one place. **That tolerance is confined to
unqualified ids**: it strips separators under a case-insensitive comparison, so applied across owners it
would let one plugin's id resolve another's. It exists for the six built-in spellings and belongs only to
them.

A descriptor carries the localized name, description, default data and, where the type is configurable, a
**data schema** — so built-ins register through the same registry and the host owns every type's
presentation. The client asks one question and gets one answer whoever provides the type, which is what
stops a plugin type being registered but unnameable. Built-in status is structural: only a type nothing
provides externally has no owner. The schema is mandatory for a configurable type because
[ADR 0050](0050-ui-sessions-are-host-brokered.md) names the failure it prevents — a tree writing into an
unvalidated payload validates, saves, and destroys the widget with nothing red anywhere.

**A provider-owned widget session opens under the provider's real id.** This is the load-bearing part:
ownership checks, session lookup and invalidation all compare against the id a provider publishes under,
so a synthetic id fails the first patch. The synthetic ids remain for built-ins only, and a provider-owned
session identifies what it is for by its surface attributes instead — which also means the resolver needs
no change at all, since a plugin id already resolves through the remote registry and an in-process
integration through its own. One consequence had to be made explicit: a drag ghost and the live tile it
is dragged from produce byte-identical surfaces, distinguished only by a suffix on the synthetic id, so
`ghost` moved onto the surface attributes.

### Folder views

The **widget grid is a real, stored, selectable id**, not the absence of one, and an absent view id reads
as it — which is what makes this need no data migration. Modelling the grid as "no view id" would leave
every reader deciding what an empty field means and leave the picker with an entry that cannot be named.

**The grid stays natively rendered.** It is addressed as a folder view but not served as one: edit mode,
drag and drop, marquee selection, the clipboard and the context menu live in the client and are not
expressible as a serialized tree. The registry therefore deliberately does *not* resolve the built-in id
— it has no provider, and answering with an empty owner would be a lie a caller could act on.

**A folder's view is its own, not inherited.** Grid appearance inherits along the parent chain; a view
does not, because a dashboard's configuration means nothing to a folder that did not choose it.

**Folder views reuse the component profile rather than getting one of their own.** One component model
across widgets, folder views and modals is the point, and a second profile would be a second thing to
document, version and keep compatible forever. The cost is stated rather than hidden: a folder view
cannot scroll, and its lengths are fractions of the box's *smaller* side, usually its height. A
page-shaped profile remains an additive option if real dashboards need one.

**Navigation is host-owned.** Macro Deck draws the back button and it always drives Macro Deck's own
navigation stack. A provider's "hidden" is a *preference*: the button appears anyway when the view is the
only thing on screen and there is a valid target. A provider that could suppress the way out could trap a
user inside a broken view, and no plugin-facing convenience is worth that.

**The choice is offered only where it leads somewhere** — more than one view registered *and* a device
that can render one. A surface the host rasterises a key grid for cannot draw a tree, so a custom view
would simply be blank on it. Like the other layout capability flags, absence reads as *yes*: saying
nothing must not read as saying no.

### Action modals

**Opening a modal and answering it are two exchanges, not one.** Showing it answers with a modal id as
soon as it is registered; the user's answer arrives later as its own capability invoke. Making the answer
the opening call's result would have required a request deadline long enough for a human — a
protocol-wide change to serve one operation.

**The client opens the session, the host does not.** The host registers the modal and tells the
originating client it exists; that client opens the session over its own authenticated connection, so the
session binds to a principal the host was told rather than one it inferred from a client id, and the
ownership rule protecting config surfaces protects modals unchanged. The first principal to open a modal
claims it, and a second gets the same answer as for a modal that does not exist, so this cannot discover
that a dialog exists on another device.

**Everything that is not an explicit completion is a cancellation** — dismissal, disconnect, flow
cancellation, session fault, the run reaching its limit, and a completion carrying no value. An action
therefore never has to tell "cancelled" from "never answered", and awaiting a modal cannot hang, which is
the property that makes this safe to offer at all. A sweep covers the one case with no session to hang a
hook on: a fire-and-forget modal a client never opened.

**A modal is gated on a live action execution of the same plugin.** Without the gate any connected plugin
could put a dialog on a user's screen whenever it liked. **The wait is bounded by the flow, not by a
second timeout of the modal's own** — a timeout chosen here could only cut short a modal the host still
considered live.

**A modal names a view; it does not carry one.** The tree is built by the provider's own `IUiProvider`,
so a modal has the same lifecycle and reactive updates a widget has. Carrying a pre-built tree would make
every modal a snapshot, and a plugin's could never be anything else.

## Consequences

- A plugin offering any of these implements two contracts. A widget type registered by a provider whose
  surfaces omit the widget surface is pickable and can never draw — the provider's own bug, surfaced as a
  rejected session rather than a refused registration, because refusing would couple registration to the
  UI handshake's ordering.
- **A registered local id is a compatibility commitment.** Widgets and folders store the qualified id, so
  renaming one strands every widget or folder using it.
- **An unregistered, withdrawn or absent type or view is a rendering state, never a data change.** The
  item keeps its stored id and configuration, is stored, exported and re-imported unchanged, and the
  session opener rejects rather than faults. Reinstalling the plugin restores everything exactly as it
  was. A catalogue entry is deliberately not withdrawn on a mere disconnect, only on uninstall — the entry
  supplies the name, default data and schema of items already on decks, and dropping it during a plugin
  restart would leave every one of them nameless. The Angular registry mirrors this: an id it carries no
  registration for falls back to the generic tree renderer.
- The six built-ins lose compile-time exhaustiveness. A `switch` over widget types now needs a default
  arm, which is the correct shape for an open set.
- A widget picker card is a live sample preview, so a plugin type's card costs a session; a provider
  serving no preview surface gets a named placeholder rather than a blank card, which is why the
  descriptor has no icon field — an icon would be frozen contract with no reader.
- Widget sessions get their own per-provider cap. The general cap was never a constraint while every
  widget had its own synthetic id, but a folder holds up to 96 widgets and they now all count against
  their plugin; bounding widget and preview surfaces separately leaves the tighter bound where it was
  earned, on config and dialog surfaces.
- **A press on a provider's widget is nothing to do, not a failure.** A provider's widget carries no flows
  for the host to run, so the press is accepted and does nothing. Executing flows out of a plugin's opaque
  widget data is a separate decision this one deliberately does not take.
- A modal cannot outlive its flow; an integration wanting a long-lived surface wants a folder view. An
  open modal occupies one concurrent run slot, and both that and the client's own limit are refusals
  rather than queues. A plugin that disconnects while its modal is open receives a cancellation, not a
  hang.
- A registered view can be invisible on a particular profile. That is a display rule, not a failure.
- Built-in integrations serve their dialogs through a host-side provider interface, because they are
  written against the SDK exactly as plugins are and sit below the host's UI layer. The provider resolves
  under the integration's own id, so nothing about the modal contract differs between a built-in and a
  plugin.
- The manifest permission vocabulary gains `host:folder-views` and `host:widget-types`, declarative like
  every other permission string.

## References

- [Issue #750](https://github.com/Macro-Deck-App/Macro-Deck/issues/750),
  [Issue #785](https://github.com/Macro-Deck-App/Macro-Deck/issues/785),
  [Issue #843](https://github.com/Macro-Deck-App/Macro-Deck/issues/843)
- [Folder views](../../docs/src/content/docs/ui/views/folder-views.md),
  [Macro Deck UI guide](../../docs/src/content/docs/ui/index.md)
