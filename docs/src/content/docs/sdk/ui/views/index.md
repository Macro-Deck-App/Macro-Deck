---
title: Views and surfaces
description: What a view and a surface are, the surface kinds Macro Deck offers, session modes, and which representation a client renders.
---

Authoring a view (covered under [Concepts](/sdk/ui/concepts/ui-model/)) is half the job; the other half
is serving it. Macro Deck renders a view inside a **session** that the host owns, against one **surface**
- a place in the app a tree can be rendered: a configuration dialog, a deck widget, a folder, a modal, or
the developer preview.

The **tree**, the **patch** that mutates it, and the **session** that carries both are transport-neutral
`MacroDeck.Ui.Model` vocabulary - the same regardless of which surface renders them. The **surface kind**
itself is Macro Deck's own vocabulary: it names a place in the *shipped app*, and is open the same way a
node type is, so a provider declining a kind it does not recognise is correct, not an error.

## Surface kinds

| kind | rendered where | session mode | covered on |
| --- | --- | --- | --- |
| `config` | An integration's config flow, a configured action instance, a folder's view, or a widget | exclusive | [Serving a configuration view](/sdk/ui/views/configuration/), [Configuring a widget](/sdk/ui/views/widget-configuration/) |
| `widget` | A deck widget | shared | [Deck widget views](/sdk/ui/views/widget/) |
| `folder` | A folder that selected a custom view | shared | [Folder views](/sdk/folder-views/) |
| `dialog` | A modal an action opened | exclusive | [Modal views](/sdk/ui/views/modal/) |
| `developer-preview` | A `[UiPreview]` scenario, opened from Developer Tools | exclusive | [Developer preview](/sdk/ui/views/developer-preview/) |

A **shared** session may have several clients attached at once - the same deck widget showing on more than
one device - and a provider never addresses one client specifically except through an event's `clientId`.
An **exclusive** session belongs to one client, which is what lets a `dialog` or `config` tree carry
entered values without another client's attach resetting them. See
[Serving a view](/sdk/ui/views/sessions/) for the full session lifecycle shared by every kind.

## Which representation a client renders

Configuration is the one place two representations of the same thing can both exist: your declared
`ConfigFlowStep.Fields`/`IActionDefinition.Parameters`, and a Macro Deck UI tree served beside them. The
client chooses one representation, never a mix of both. It negotiates the UI model version locally,
before a session is opened, so a client that cannot render your tree costs you nothing - no session, no
slot - and falls back to the declared fields. See [Serving a configuration view](/sdk/ui/views/configuration/)
for what that means for a provider.

Every other surface kind has no non-tree fallback: a `widget`, `folder` or `dialog` provider that declines
or fails simply has nothing rendered for that surface.
