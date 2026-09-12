---
title: Icon
description: One glyph of Macro Deck's built-in icon set, drawn by name in one colour, with no resource to upload.
---

One glyph from Macro Deck's own icon set, named rather than uploaded. It is drawn as a single-colour mask,
so it takes a theme role or a literal colour like text does.

`ui.icon`

## Example

```csharp
new UiIcon
{
    Key = "state",
    Icon = UiValue.From(() => state.Value.Playing ? UiIcons.Pause : UiIcons.Play),
    Size = 0.4,
    Role = UiComponentTextRoles.Primary,
    Fallback = new UiTextRun { Key = "stateLabel", Text = UiText.From(() => state.Value.Playing ? "Pause" : "Play") },
}
```

![A white play glyph centred in a tile](../../../../assets/ui/icon.png)

A play/pause glyph: switching it is a `set-properties` patch carrying `icon` alone.

## Colour

```csharp
Color = "#34c759",
```

`Role` picks a theme role and follows the viewer's theme; `Color` is a literal colour that overrides it. See
[Colours and text](/ui/concepts/theming/).

## Names and versions

A reader draws a glyph only for a name it carries. Names are published in groups: group 1 is
`UiIcons.Version1` below, and every later name arrives in a new group that raises `ui.icon`'s maximum
component version. A name is never removed or renamed.

Use the `UiIcons` constants rather than string literals. For a name above group 1, ask for its version and
carry a fallback, so an older reader draws the fallback instead of an empty box:

```csharp
RequiredComponentVersion = UiIcons.VersionOf(name),
Fallback = new UiTextRun { Key = "label", Text = "Wi-Fi" },
```

`UiIcons.VersionOf` returns `1` for every name listed here and `null` for a name no version draws. A
`RequiredComponentVersion` of `1` or `null` asks for nothing beyond knowing the type.

## Published names

Component version 1 draws these 76 names:

`action-button-type`, `alert-triangle`, `align-bottom`, `align-center`, `align-left`, `align-middle`,
`align-right`, `align-top`, `arrow-down`, `arrow-left`, `arrow-right`, `arrow-up`, `bell`, `braces-x`,
`bug`, `chart`, `check`, `chevron-right`, `clipboard`, `clock-type`, `code`, `copy`, `crosshair`,
`device-desktop`, `device-floppy`, `device-phone`, `device-tablet`, `disc`, `discord`, `dots-vertical`,
`download`, `external-link`, `file-text`, `folder`, `folder-plus`, `globe`, `grid`, `heart`,
`history-graph-type`, `image`, `info`, `layers`, `list-play`, `lock`, `log-out`, `message-square`, `minus`,
`moon`, `music-note`, `music-player-type`, `pause`, `pencil`, `pin`, `pin-off`, `play`, `plus`, `power`,
`puzzle`, `refresh`, `scissors`, `search`, `settings`, `sidebar`, `sliders`, `star`, `store`, `sun`,
`trash`, `undo`, `unlock`, `upload`, `user`, `weather-type`, `wifi`, `x`, `zap`.

The C# constant is the name in PascalCase - `UiIcons.AlertTriangle` is `alert-triangle`. The list is
[`UiIcons`](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/ui-model/src/MacroDeck.Ui/Components/UiComponentValues.cs);
[ADR 0084](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0084-built-in-icon-names-are-a-versioned-public-vocabulary.md)
records why it is frozen.

## Properties

| Property | Values | Default (absent) | Meaning |
|---|---|---|---|
| `Icon` (`icon`) | a `UiIcons` name | Draws nothing | The glyph. |
| `Size` (`size`) | length | The box's smaller side | The edge of the square the glyph is drawn in. |
| `Role` (`role`) | `UiComponentTextRoles` | `primary` | The theme colour; ignored when `Color` is present. |
| `Color` (`color`) | `#rrggbb` | Uses `Role` | A literal colour overriding `Role`. |
| `MainSize` (`mainSize`), `Fill` (`fill`) | - | - | Shared with every leaf - see [Sizing](/ui/concepts/sizing/). |

## Events

None. Put the icon inside a [button](/ui/components/button/) to press it.

## Children

None. `ui.icon` is a leaf.

## Layout

On its parent's main axis an icon is `Size` long unless `MainSize` or `Fill` says otherwise. The glyph is
centred in its box. See [Sizing](/ui/concepts/sizing/).

## Reader behaviour

- Draw the glyph as a single-colour mask in a `size` square centred in the box, in `color`, else `role`, else
  the primary text colour.
- Draw nothing for a name outside the groups the reader carries - never a placeholder square.
- Advertise as the maximum component version the number of name groups carried.
- A reader that does not know `ui.icon` draws the node's `fallback`, typically a `ui.text` saying what the
  icon meant:

```json
{
  "type": "ui.icon",
  "properties": { "icon": "play", "size": { "basis": 0.4 } },
  "fallback": { "type": "ui.text", "properties": { "text": "Play" } }
}
```

## See also

- [Image](/ui/components/image/) - your own artwork
- [Resources](/ui/reference/resources/)
- [Colours and text](/ui/concepts/theming/)
- [Compatibility](/ui/reference/compatibility/)
