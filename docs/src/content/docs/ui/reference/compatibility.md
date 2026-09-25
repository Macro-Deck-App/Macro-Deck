---
title: Compatibility
description: The three packages as versioned contracts, why the model major moved to 3, and why 4 did not move the floor with it.
---

`MacroDeck.Ui`, `MacroDeck.Ui.Model`, and `MacroDeck.Ui.Testing` are public NuGet contracts. Existing
keys, component meanings, patch semantics, and public API members must follow the normal
[compatibility policy](/policies/compatibility/).

## Why the floor is 3 and the ceiling is 4

The UI model's `Minimum` is 3 and its `Current` is 4, and the gap between them is the difference between
a rename and a widening.

The floor moved to 3 because the component vocabulary was renamed outright rather than extended: every
`widget.*` node type became either `ui.*` or `macrodeck.*`, with no alias kept for the old spelling. A
tree built against the old vocabulary is not representable under the new one, so a package that no longer
understands a single `widget.*` type must not advertise the majors in which those were the only spelling.

The ceiling moved to 4 because an `icon` property may now carry a typed `{"type":…,"reference":…}`
provider reference where it previously always carried a bare icon-pack reference string - the same shape
as the move to 2, where a text property gained the option of carrying a localization reference. A producer
built against 3 emits only the string form and one built against 4 may emit either, so the two are not
interchangeable and the version has to say which a session speaks. The floor stayed where it was because
nothing was renamed: a reader at 4 accepts both readings, so it still reads every tree a producer at 3
emits, and `UiIconInput` is unchanged.

Either way a client and a provider negotiate the model version as they always have - before a session is
opened - and a mismatch produces the same graceful decline, never a tree the reader cannot parse.

## Additions that moved no version

New component types and new properties are additive and leave the model version alone; the component
profile's rule decides which of the two a new feature is. [Modifiers](/ui/components/modifier/) add both:

| Addition | Shape | An older reader |
|---|---|---|
| `modifiers` (background, radius, border, accessibility text, `disabled`) | A property on any node | Ignores it and draws the node plainer. A disabled subtree still offers none of its own events, because the DSL stopped it declaring them, but the reader does not know the region absorbs the tile's press, so a deck tile's own flows still run there. |
| `drag`, `drag-end`, `swipe`, `pinch`, `pinch-end` | Event names | Never sends a name it does not implement. |
| `pointer-down`, `pointer-move`, `pointer-up`, `tap` | Event names | Never sends them, and does not claim the pointer for a node that declares only these: a deck tile's own press flow still runs when the node is pressed there. |
| `offersStateProvider`, `offersIconProvider`, `stateProviderBlockId`, `iconProviderBlockId` on `actions-list-editor`, and the `provide` config event | Properties and an event name | Ignores the properties, never sends `provide`, and shows the action list without provider controls. |
| `status` (`UiStatus`), `menu` (`UiConfigMenu`) and `dialog` (`UiConfigDialog`) | Configuration types | Declines them and draws the node's `fallback`. |
| `confirmTitle`, `confirmMessage`, `confirmLabel`, `confirmDanger`, `promptValue` on `button` | Properties | Raises `activate` at once, without asking and without a payload. |
| `interaction` on `ui.slider` (`relative`) | A property | Ignores it and keeps the absolute drag: a press jumps the level to the pointer, and a tap sends `change`. |
| `ui.modifier` (padding, opacity, clip, mask, frame), component version 1 | A type | Draws the node's explicit `fallback`; without one, none of the wrapped content (Macro Deck's renderer shows a faint placeholder box). No fallback is invented for you. |
| `ui.responsive` and its `variants` property, component version 1 | A type | Draws the node's `fallback`. Unlike `ui.modifier`, one is invented when you set none: a copy of the default layout. When it decides whether the tile's own press belongs to a control, it walks every layout, not only the one it would have drawn - see [Responsive](/ui/components/responsive/#older-readers). |

See [ADR 0064](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0064-components-are-a-registry-over-two-namespaces.md)
for why the vocabulary is organized as a registry over the `ui.*`/`macrodeck.*` namespaces rather than one
flat list, and [The UI model](/ui/concepts/ui-model/#model-version-negotiation) for where negotiation
sits in a session's lifecycle.

## Behaviour changes that moved no version

These change what an existing plugin observes without a new model version. Each is a deliberate
exception to the [compatibility policy](/policies/compatibility/), listed here so you can check your plugin
against it.

| Change | Hosts | What an existing plugin sees |
|---|---|---|
| A plugin built on the SDK with the message channel declares the `messaging` capability kind, at local id `provider`, whenever the host lists it in `capabilityKinds` - also when the plugin never uses the channel | Released after 3.0.0-beta.11 | A test that asserts a plugin's exact declared capabilities, against `MacroDeckTestHost`, `PluginTestHarness` or a real host, sees one more entry. Nothing changes on a host without the kind. See [Messaging between plugins](/features/messaging/#older-versions-of-macro-deck). |
| `UiView` implements `IDisposable`, and `UiPreviewInstance.DisposeAsync` disposes its view - including a `UiView` a preview scenario returned itself | Released after 3.0.0-beta.7 | A plugin that never disposes a view behaves as before. A preview scenario that returns a cached `UiView` gets a disposed view that ignores every event from the second open on; build a new one per call. A plugin that builds with CA1001 as an error may see it on a type that creates a `UiView` in a field without being disposable. See [Disposing](/ui/concepts/reactive-updates/#disposing). |
| A `link` with an external `http` or `https` URL is opened by the host in the default browser on every surface, whether or not it declares `activate` | Released after 3.0.0-beta.6 | A link that declares `activate` still receives it exactly once, and is now opened by the host outside the integration setup dialog too. A handler that opened the URL itself opens a second tab; remove that code. See [Links](/ui/concepts/events/#links). |
| `IUiConfigFlow.CreateUiSessionAsync` can be called again for the same flow instance, after its previous session ended with a retryable error or was reloaded for .NET Hot Reload | Released after 3.0.0-beta.13 | A flow that assumed one session per flow instance gets a second call. Build each session from state the flow holds; the user's unsaved edits arrive as `change` events. See [From a config flow](/ui/views/configuration/#from-a-config-flow). |
| Under .NET Hot Reload, a plugin built on this SDK ends every open session except dialogs with `ui/reload`, and Macro Deck opens each one again | SDK released after 3.0.0-beta.13; on an older host the SDK sends `ui/fault` instead | A provider is asked for new sessions for views that are already open, each time Hot Reload applies a change. A plugin that is not being hot reloaded sees nothing new. See [Real views](/ui/views/developer-preview/#real-views). |
| `PackageSigner` and `PackageVerifier` refuse an icon pack with more than 30,000 archive entries, counting the signature files, with `too-many-entries`; an icon pack's `pack.json` may be up to 32 MiB instead of 8 MiB | SDK released after 3.0.0-beta.13 | Signing a pack that would exceed 30,000 entries now fails. No host ever imported more than 10,000 entries, so no signed pack that installs is affected. Plugin, profile and template manifests keep the 8 MiB bound. See [Publish an icon pack](/creator-portal/publish-icon-pack/#size-limits). |
| A `ui.list` reader starts its `reveal` count over when the list's content is replaced: it holds fewer children than at its previous paint, or the child at the furthest index sent is gone or has another id | Released after 3.0.0-beta.11 | A `reveal` can now carry an index at or below one the plugin already received, after its list shrank or its rows were replaced, including a row inserted or removed above the furthest index. A handler that only grows its window, as [List](/ui/components/list/#loading-more-items) shows, is unaffected; one that sets its window from the index unconditionally can shrink it and should keep the larger value. A reader that has not adopted this rule, such as an older Companion app, may not ask again after a replacement. |
