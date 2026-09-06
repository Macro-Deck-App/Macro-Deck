---
title: Macro Deck UI
description: A general framework for declarative UI, rendered across every surface Macro Deck offers - configuration, deck widgets, folder views and modals.
---

Macro Deck UI is a framework for describing what a person sees and can act on, once, as a tree - and
letting Macro Deck render that tree wherever it is needed. `MacroDeck.Ui.Model` is the transport-neutral
tree, event and patch contract; `MacroDeck.Ui` is the declarative C# DSL and reactive runtime built on it;
`MacroDeck.Ui.Testing` renders and asserts against a view headlessly. Use the model package when you need
the low-level tree/patch contracts directly. Use `MacroDeck.Ui` when you want to author a view.

The same tree renders in a config flow, an action's configuration, a deck widget, a folder view and a
modal dialog - see [Views and surfaces](/sdk/ui/views/). What differs by surface is layout and available
screen space, never the vocabulary you author in.

## One vocabulary, two namespaces

Every node type is either `ui.*` or `macrodeck.*`, and the split follows one rule: a component is
`macrodeck.*` when a reader cannot draw it from the tree alone, because it must resolve a Macro
Deck-defined reference - a time or a media position - against its own clock. Everything else is
`ui.*`, however Macro Deck-flavoured its styling. See
[ADR 0064](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0064-components-are-a-registry-over-two-namespaces.md)
for why the vocabulary is a registry over these two namespaces rather than one flat list, and the
[component reference](/sdk/ui/components/) for the full catalog.

## Install

Plugin project:

```xml
<PackageReference Include="MacroDeck.Ui" Version="3.0.0" />
```

Test project:

```xml
<PackageReference Include="MacroDeck.Ui.Testing" Version="3.0.0" />
```

## A small view, end to end

A view is composed from elements with stable keys. Inputs bind to state:

```csharp
var apiKey = new UiState<string>(string.Empty);

var view = new UiFlow
{
    Key = "setup",
    Children =
    [
        new UiStep
        {
            Key = "credentials",
            Children =
            [
                new UiStringInput
                {
                    Key = "apiKey",
                    Label = "API key",
                    Binding = Bind.To(apiKey),
                },
            ],
        },
    ],
};
```

`UiViewBuilder.Build(...)` creates a one-time tree. `UiView` keeps the view reactive and emits patches
when state changes. Serving that view to a client is a separate step - see
[Serving a view](/sdk/ui/views/sessions/).

## Where to go next

- **[Concepts](/sdk/ui/concepts/ui-model/)** - the tree/patch model, state and bindings, events, reactive
  updates, sizing and theming.
- **[Components](/sdk/ui/components/)** - the full `ui.*`/`macrodeck.*` catalog, one page per family.
- **[Views](/sdk/ui/views/)** - the surfaces a tree renders on: sessions, configuration, deck widgets,
  folder views, modals and the developer preview.
- **[Reference](/sdk/ui/reference/patches/)** - patch operations, resource handles and the compatibility
  contract across the three packages.

## Related documentation

- [SDK reference](/sdk/)
- [Capabilities](/sdk/capabilities/)
- [Folder views](/sdk/folder-views/)
- [Testing plugins](/sdk/testing/)
- [ADR 0038](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0038-ui-model-and-declarative-dsl.md)
- [ADR 0064](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0064-components-are-a-registry-over-two-namespaces.md)
- [ADR 0065](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0065-the-component-profile-authoring-contracts.md)
