---
title: Configuring a widget
description: Describing a widget's configuration as two named regions Macro Deck lays out, and reaching the editors the app already ships.
---

A widget's configuration is a Macro Deck UI tree like any other, served on the `config` surface under the
`widget-config` entry point. What makes it its own page is the shape of that tree: it is **two named
regions**, and Macro Deck owns everything around them.

You supply the fields. Macro Deck supplies the widget preview, the split layout and its narrow-window
drawer, the visual and JSON modes, scrolling, the save affordance and the unsaved-changes prompt. Nothing
in the contract names a renderer, so the same tree is what a future native client will draw.

## The two regions

```csharp
public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
{
    if (request.Surface.Kind != UiSurfaceKinds.Config) return Task.FromResult<IUiSession?>(null);

    return Task.FromResult<IUiSession?>(new MyWidgetConfigurationSession(request.Surface));
}
```

and the tree that session builds:

```csharp
new UiWidgetConfiguration
{
    Key = "root",
    Properties = new UiWidgetProperties
    {
        Key = "properties",
        Children =
        [
            new UiStringInput { Key = "label", Label = MyStrings.Label(), Binding = Bind.To(label) },
            new UiBooleanInput { Key = "showSeconds", Binding = Bind.To(showSeconds) },
        ],
    },
    Editor = new UiWidgetEditor
    {
        Key = "editor",
        Children = [new UiActionsListEditor { Key = "flows", Binding = Bind.To(flows) }],
    },
};
```

`Properties` is the ordinary field list - the desktop editor draws it beside the preview.
`Editor` is the room a field list would not fit in, and it is **optional**: a widget that needs none
serves a root with one child, and the editor is a single pane rather than a split with an empty half.

Which side of a window each region lands on is the renderer's decision, not yours. Name the regions and
let it place them.

## An input's id is the data key it configures

A region opens no input-id scope, so a top-level input's id is the widget data key it writes - the same
rule a config flow's top-level input follows for the field key it submits. That holds across both
regions: they share one namespace.

Nesting is expressed with `UiObjectInput` and `UiArrayInput`, which do open a scope, so a `style` input
inside an object keyed `border` configures `border.style` and a repeated `UiArrayInput` keyed `states`
configures a JSON array. Items are addressed by their own stable key and never by position - a positional
id destroys focus and in-flight edits on every reorder.

## The tree renders a draft it does not own

Nothing your tree does persists anything. Macro Deck accumulates the edits and writes them through the
ordinary widget save path when the user saves, which is what keeps schema validation, the JSON mode and
the unsaved-changes prompt working exactly as they did - see
[ADR 0050](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0050-ui-sessions-are-host-brokered.md).

The widget's stored configuration arrives on the surface rather than being looked up, because a plugin
cannot read the host's stored widgets. `UiConfigSurfaceAttributes` names the keys: `widgetId`,
`widgetType` and `widgetData`. A key your tree never mentions is preserved untouched through a save, so
you can serve a partial configuration without dropping the rest.

## Reaching an editor Macro Deck already has

Some configuration is not a field. Rebuilding the action editor - selection, ordering, nesting, drag and
drop, triggers, conditions - out of primitives would be a large amount of work per renderer, so the
configuration profile names it instead:

| node type | configures |
| --- | --- |
| `actions-list-editor` | The list of action flows, with its triggers, ordering and nesting |
| `action-picker` | One action from the catalog of everything installed |
| `variable-picker` | One variable, optionally narrowed to a type or to writable ones |
| `device-picker` | One connected device |
| `integration-picker` | One integration, or one of its configuration entries |
| `icon` | One icon, as a typed provider reference |

A renderer maps each onto the editor it already ships, and a plugin ships no renderer code for any of
them.

`device-picker` is the one with a caveat: the device list is an administrative endpoint, so the control
can only populate where the viewer holds admin scope - the desktop editor does. Prefer one of the others
where your widget has a choice.

**A renderer that has no such editor declines the type**, exactly as it declines any node type it does
not know, and draws the node's `Fallback` instead. If your widget must stay configurable on a renderer
that lacks one of these, give the node a fallback built from primitives - a `json` input over the same
value is usually enough to keep the widget editable rather than blank.

## Declining

Returning `null` from `CreateSessionAsync` declines, which is not an error. Unlike an action or a config
flow, a widget has **no declared field list to fall back to** - so a declined widget configuration leaves
the user with the editor's JSON mode and nothing else. Decline only surfaces you genuinely do not
configure, and check `widgetType` rather than assuming the surface is yours.
