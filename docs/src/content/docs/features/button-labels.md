---
title: Button labels
description: Change one of an action button's labels from a plugin with WidgetAppearancePatch.LabelNumber, and read every label a device has to draw.
---

An action button can carry up to four labels. The first is its primary label, the one a button has always
had; the user adds the others in the button editor's **Label** tab. Each label has its own text, font,
size, colour and position.

## Quick start

```csharp
await context.Widgets.ApplyAsync(new WidgetAppearanceRequest
{
	WidgetId = widgetId,
	Patch = new WidgetAppearancePatch { LabelNumber = 2, Label = "{{ vars.cpu }} %", LabelColor = "#ef4444" },
});
```

`LabelNumber` names the label a patch changes: `1` is the primary label, `2` to `4` are the extra labels
in the order the editor lists them. Leave it unset to change the primary label, as every patch did
before.

## What a numbered patch changes

- **It moves the label fields only:** `Label`, `LabelColor`, `FontFaceId`, `FontSize`, `TextAlign` and
  `LabelPosition`. Background, icon, border and accent colour belong to the button and ignore the number.
- **`TextAlign` and `LabelPosition` are preset positions.** A label sits at a free position between 0 and
  100 percent on each axis; `left`, `center`, `right` and `top`, `center`, `bottom` are 0, 50 and 100.
  Setting one replaces whatever free position the user chose on that axis, and clearing `Font` returns
  the label to the centre.
- **Clearing follows the number too.** `ClearProperties` with `Label`, `LabelColor` or `Font` resets that
  label. Clearing a label the button does not have changes nothing.
- **A label that does not exist yet is created**, together with any before it, with no text. A patch
  that only colours label 3 therefore leaves an invisible label behind, which the user sees in the
  editor's list.
- **A number outside `1` to `4` rejects the whole request** and `ApplyAsync` returns `false`.
- **In state mode the label belongs to the targeted state.** Every state has its own extra labels and
  never shows the button's or another state's. `StateIds` picks the states as for any other patch.
- **Text is a template like the primary label's.** Each label is rendered on its own, so one that cannot
  be shown displays `n/v` while the others stay as they are.
- **Other widget types ignore the number.** They have the one label.
- **An older Macro Deck ignores `LabelNumber` and changes the primary label.** Nothing tells a plugin
  which kind of host it is talking to, so send a numbered patch only where overwriting the primary label
  would be acceptable.

The **Set Label**, **Set Label Color** and **Set Font** actions offer the same choice to users as **Label
to change**.

## On a device

A device provider receives the labels resolved: `DeviceSurfaceAppearance.Label` for the primary one,
`ExtraLabels` for the others, and `LabelX`/`LabelY` for the primary label's free position. See
[Devices](/features/devices/#receiving-and-rendering-a-session).

## Over the plugin protocol

| Wire field | SDK member |
| --- | --- |
| `labelNumber` on the `widgets` appearance patch | `WidgetAppearancePatch.LabelNumber` |
| `labelX`, `labelY`, `extraLabels` on the `devices` surface appearance | `DeviceSurfaceAppearance.LabelX`, `LabelY`, `ExtraLabels` |

All four are optional and additive within protocol major `3`; see the
[protocol change log](/reference/protocol/).

## See also

- [Button states](/features/button-states/) and [Button icons](/features/button-icons/)
- [Devices](/features/devices/)
- [Compatibility](/policies/compatibility/)
