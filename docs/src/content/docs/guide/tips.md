---
title: Tips and tricks
description: Smaller things worth knowing when using Macro Deck.
---

## Editing the deck

Unlock the deck to edit it, lock it to press buttons.

| Shortcut | Does |
| --- | --- |
| Ctrl/Cmd + A | Select all widgets |
| Ctrl/Cmd + C, X, V | Copy, cut, paste widgets |
| Delete | Delete the selection |
| Esc | Clear the selection |
| Shift + click on Save | Save and close the widget editor |

- **Select several widgets** by dragging a box around them. Hold Ctrl/Cmd to add to the selection.
- **Paste somewhere specific:** right-click a free cell.
- **Try a button without opening it:** right-click it and choose **Run**. It runs the Short Press actions
  as if you pressed the button, so a button set to **Cycle states on tap** also moves to its next state.
- **Drop an image, an app or a shortcut** on a tile to use it as the background.

## Backgrounds

- **Color a widget's background:** open the widget and pick a **Background Color**.
  Action Button, Slider, Clock, Countdown, Stopwatch, Weather, History Graph, Gauges, Music Player,
  Twitch Chat and YouTube Chat all have one.
  **Reset** returns to the default look; on a Music Player that is the album art's color.
- **Let the folder background show through:** open the color field and choose the checkered
  **Transparent** swatch. The tile loses its own background and shadow, the border stays.
- **Set Background Color** changes it from an action, and **Reset** there clears it again.
- **Style an Action Button's label:** in the **Label** tab, turn off **Label Shadow**, give the text an
  outline with **Label Outline** and the width next to it, or frame it with **Label Box Border** and its
  width. A width can be changed once its color is set. Each state can have its own. Hardware devices and
  the Companion app keep the default label look for now.
- **Hide empty cells:** set **Empty cells** to **Transparent** in the profile's settings, and the running
  deck shows only its widgets on the folder background. A folder's grid settings can override it with
  **Visible** or **Transparent**; **Inherited** takes the value from the parent folder, then the profile.
  While you edit the deck, hidden empty cells show a dashed outline, so you can still place widgets. The setting
  does not apply to hardware devices.

## Your color palette

Every color field opens a popover with a color picker and your color palette, which starts with Macro
Deck's default colors. Pick a color and select **+** to add it, and it shows up in every color field from
then on, including those of plugins. To remove one, hover over it and select its **×**, or focus it and
press Delete; this works for the default colors too. **Settings > Appearance > Color palette** lists every
color, lets you add and remove them, and **Restore default colors** brings the defaults back. The palette
holds up to 24 colors. A field that cannot be translucent only shows the palette's opaque colors.

## Colors from a variable

Keep your colors in **Color** variables and change a whole deck at once. Create a user variable of type
**Color** on the **Variables** page, for example `primary` with `#3366ff`. Its value is `#rrggbb`, or
`#rrggbbaa` for a translucent color; the **Opacity** slider sets the last two digits.

Every color picker of a widget, a color threshold band, a folder's or profile's grid **Background**, the
**Accent** color in **Settings > Appearance** and the color of an action such as **Set Background Color**
opens a popover when you select it; switch its tab from **Color** to **Variable**. Choose a
**Color variable**, then **Add modifier** to derive a shade from it:

| Modifier | Does |
| --- | --- |
| **Brighten**, **Darken** | Makes the color lighter or darker by **Amount** percent |
| **Set opacity** | Sets how opaque the color is, from 0 to 100 percent |
| **Increase opacity**, **Reduce opacity** | Makes the color more opaque or more transparent by **Amount** percent |
| **Increase saturation**, **Reduce saturation** | Makes the color more vivid or greyer by **Amount** percent |
| **Shift hue** | Turns the color around the color wheel by **Amount** degrees |
| **Mix** | Blends in another color or color variable, chosen under **Mix with**, by **Amount** percent |

Modifiers apply from top to bottom, so a button can use `primary` darkened by 20 % at 70 % opacity while a
slider uses `primary` as it is. Change the variable, by hand or with **Set Variable**, and every widget,
folder and the accent color that use it update right away.

- The accent color stays opaque, so the opacity modifiers have no effect there.
- An action's color is worked out when the action runs. **Set Background Color** with a variable writes the
  color the variable has at that moment; the widget does not keep following the variable.
- If the variable is deleted, unavailable or no longer a **Color**, the field falls back to the theme's color.
  A color threshold band whose variable is missing has no color of its own: that range shows the widget's
  own color, and the other bands keep theirs.
- Only **Color** variables work. A **Text** variable that holds `#3366ff` is not offered and does not work in
  a template either; create a Color variable instead.
- The same works in text, as a template: `{{ vars.primary | color | color_darken: 20 | color_opacity: 70 }}`
  gives the color as `#rrggbbaa`. The other modifiers are `color_lighten`, `color_saturate`,
  `color_desaturate`, `color_increase_opacity`, `color_reduce_opacity`, `color_hue` and
  `color_mix: "#ffffff", 50` or `color_mix: vars.other, 50`. A fixed color works too:
  `{{ "#ff0000" | color_darken: 10 }}`.
- Hardware devices show these colors without transparency. Older versions of the Companion app show the
  theme's color instead of a translucent one.
- Colors with transparency now look translucent on the deck. A color stored as `rgba(...)` or `#rrggbbaa`,
  for example by a plugin, used to be drawn fully opaque.

:::caution
If you go back to an older Macro Deck version after creating a Color variable, that version cannot read your
variables and starts without any of your user variables. Delete your Color variables before you go back.
:::

## Colors from the playing album

The **Music Player** integration offers an **Album color** Color variable for every music player you have set
up, for example Spotify or each SinusBot you added. Its value is the average color of the cover of that
player's current song. Choose it as the **Color variable** of a widget's background, then use **Add
modifier** to derive text and icon colors that stay readable, for example **Darken** or **Brighten** the album
color, or **Mix** it with white.

- Each player has its own variable, so a widget follows exactly the player you pick. A player you add later
  gets its variable as soon as it appears.
- The variable keeps the last cover color when the player is paused, stops or disconnects, and changes when
  the next cover shows up. It only has no value until the player has shown a cover once since Macro Deck
  started; then fields that use it show the theme's color. A field that follows a variable does not remember
  the color you picked before, so set it again if you stop using the variable.
- The colors of icons and text do not adjust on their own to the album color; pick modifiers that suit your
  covers.
- If you turn the **Music Player** integration off under **Integrations**, the variables are unavailable too.

## Finding a widget type

When you add a widget, type in the search field to filter by name, description or integration. Widgets
from a plugin show the plugin's name as a badge; widgets that come with Macro Deck, such as Twitch or
Calendar, show none. Click the **star** on a widget type to keep it at the top of the list for everyone who
edits this Macro Deck, and switch between tiles and a compact list next to the search field.

## Pinned widgets

Pin a widget to show it in **every folder of the profile**, or in **a folder and its subfolders**.
Good for a clock or a **Go Back** button.

## Variables in text

Labels and many action parameters accept variables:

```
Deaths: {{ vars.deaths }}
```

Random values work the same way. Use `{{ math.random 1 7 }}` for a whole number from 1 to 6 (the maximum is
not included) and `{{ math.uuid }}` for a random unique id. Put one in the value of a **Set Variable** action
to store a new random number in a variable each time the action runs.

## A folder that opens by itself

**Automatic activation** opens a folder on a device when an app gets focus, for example your
**Photoshop** folder, and can return to the previous folder when you switch away.

## Actions

- Copy and paste actions between flows with Ctrl/Cmd + C and V.
- Drag actions to reorder them.
- **Run** in the editor runs the actions right away, without pressing the button.
- Use **Switch** instead of several **If / Else** blocks when one value has many outcomes, for example a
  button that does something different for each repeat mode of a music player.
- **Color thresholds** color a slider, history graph or gauge by its value: green, yellow, orange and red by default.
  Drag a handle on the bar to move a limit, click the bar to add a limit there, and select a range to recolor
  or remove it. **Reset to defaults** brings back the standard ranges.
- **Set Accent Color** recolors a slider or history graph while its color thresholds are off. Put it inside an
  **If / Else** on a value for a rule thresholds cannot express. **Reset** returns to your theme color.
- **Control any OBS output:** **Start Output**, **Stop Output** and **Toggle Output** work on every output OBS
  lists by name, including the ones OBS plugins add, such as a multi-RTMP stream or a second recording.
  **Get Output State** writes whether an output is active into a variable, and **Toggle Output** can drive the
  button's states. An output you chose that OBS does not list right now stays selected and is marked
  unavailable, and running the action then reports that the output does not exist. These actions need an OBS
  whose obs-websocket is version 5.7 or newer; with an older one they report that.

## Share and back up

- Export a widget, folder or profile and import it anywhere, or share the file.
- **Settings > Backups** makes backups on demand and automatically before every update. See
  [Backups](/guide/backups/).

## Test automations

**Developer Tools > Trigger event** fires an event for real. Every automation listening to it runs.

## Language

**Settings > Language** switches every connected client at once, without a restart. Macro Deck is
available in English, German, French, Spanish, Italian, Dutch, Brazilian Portuguese, Czech, Polish,
Russian, Ukrainian, Turkish, Arabic, Hindi, Indonesian, Japanese, Korean, Simplified Chinese and
Traditional Chinese. **System** follows the operating system's language and uses English when Macro Deck
does not have it.

In Arabic the app reads right to left. Your deck keeps its layout: buttons stay where you placed them in
every language.
