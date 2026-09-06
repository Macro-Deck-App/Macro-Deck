---
title: Stack and layer
description: ui.stack lays children beside each other along one axis; ui.layer draws them on top of each other through the depth of the box.
---

`ui.stack`, `ui.layer`

## Purpose

`ui.stack` is the one structural container the framework ships: it lays its children out along one axis,
beside each other. Nesting stacks is how a view expresses everything a per-child margin property would
otherwise be needed for - there is no margin property on any element.

`ui.layer` solves a different problem: putting one element visually behind another. A stack can only put
children beside each other on its axis, so until a tree needs a backdrop that is itself an element - not
just a resource - or content that has to centre on the whole box rather than on whatever space earlier
children left over, a stack is enough. `ui.layer` draws every child across the whole content box, in
declaration order, the first one furthest back.

`ui.layer` is documented here rather than on its own page because it is defined entirely by contrast with
`ui.stack`, and because inset content on a layer needs a stack wrapped around it anyway - see below.

## Properties

### `ui.stack`

| Property | Meaning | Absent means |
|---|---|---|
| `direction` | The layout axis - `vertical` or `horizontal` | `vertical` |
| `justify` | How free space is distributed along the main axis - `start`, `center`, `end`, `space-between` | `start` |
| `align` | How children are aligned on the cross axis - `start`, `center`, `end`, `stretch`, `baseline` | `stretch` |
| `gap` | The gap between children, a length | No gap |
| `padding` | Inner padding on every edge, a length | No padding |
| `background` | The stack's own fill, as `#rrggbb` | The stack paints nothing behind its children |

`background` is a literal colour rather than a theme role because it encodes something a person or a
producer chose, not the theme - see [Colours and text](/sdk/ui/concepts/theming/).

### `ui.layer`

`ui.layer` carries none of its own properties. It has no padding, gap, justify or align: a layer that
needs any of those wraps the child that needs it in a `ui.stack`, which is also what lets two layers inset
their content differently from each other.

### Shared with every container

Both carry the properties every container in the framework does - `mainSize`, `fill` and, on a surface that
can complete a dialog, `answer`. See [Sizing](/sdk/ui/concepts/sizing/) for how `mainSize` and `fill`
resolve, and [Events](/sdk/ui/concepts/events/) for how a container answers a dialog.

## Supported children

Both are containers: any element, any number of children, in any combination.

## Events and interactions

Neither declares events of its own. A stack or a layer can still complete a dialog through `answer` when a
descendant's press claims it - the affordance belongs to the declared event, not to the node type, so a
list row wants a stack's absent background rather than a button's accent while still settling the dialog
it sits in.

## Layout behaviour

A stack divides its own main-axis extent among its children: each child's `mainSize` or `fill` competes for
that budget, and a child that declares neither is measured from what a renderer can work out without a
font - see [Sizing](/sdk/ui/concepts/sizing/) for the exact rule and why a text needs its own `mainSize`
whenever a filling sibling sits next to it.

A layer gives every child the whole content box regardless of what any child declares - `mainSize` and
`fill` mean nothing on a layer's children, because there is no axis to divide.

## Examples

A vertical stack with a heading and a filling row beneath it:

```csharp
new UiStack
{
    Key = "card",
    Direction = UiComponentDirections.Vertical,
    Gap = UiSize.FromBasis(0.03),
    Padding = UiSize.FromBasis(0.06),
    Children =
    [
        new UiTextRun { Key = "title", Text = UiText.Of("Now playing"), Size = 0.14, Weight = UiComponentTextWeights.Bold },
        new UiStack { Key = "body", Direction = UiComponentDirections.Horizontal, Fill = true, Children = [trackRow] },
    ],
}
```

A chart sitting behind its own label, inset differently from the chart itself:

```csharp
new UiLayer
{
    Key = "card",
    Children =
    [
        new UiChart { Key = "chart", Points = points, PlotTop = 0.66 },
        new UiStack { Key = "labels", Padding = 0.07, Children = [title] },
    ],
}
```
