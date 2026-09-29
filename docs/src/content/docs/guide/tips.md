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
  Action Button, Slider, Clock, Countdown, Stopwatch, Weather, History Graph, Music Player and Twitch Chat
  all have one.
  **Reset** returns to the default look; on a Music Player that is the album art's color.
- **Let the folder background show through:** choose the checkered **Transparent** swatch. The tile loses
  its own background and shadow, the border stays.
- **Set Background Color** changes it from an action, and **Reset** there clears it again.
- **Hide empty cells:** set **Empty cells** to **Transparent** in the profile's settings, and the running
  deck shows only its widgets on the folder background. A folder's grid settings can override it with
  **Visible** or **Transparent**; **Inherited** takes the value from the parent folder, then the profile.
  While you edit the deck, hidden empty cells show a dashed outline, so you can still place widgets. The setting
  does not apply to hardware devices.
## Finding a widget type

When you add a widget, type in the search field to filter by name, description or integration. Widgets
from an integration or plugin show its name as a badge. Click the **star** on a widget type to keep it at
the top of the list for everyone who edits this Macro Deck, and switch between tiles and a compact list
next to the search field.

## Pinned widgets

Pin a widget to show it in **every folder of the profile**, or in **a folder and its subfolders**.
Good for a clock or a **Go Back** button.

## Variables in text

Labels and many action parameters accept variables:

```
Deaths: {{ vars.deaths }}
```

## A folder that opens by itself

**Automatic activation** opens a folder on a device when an app gets focus, for example your
**Photoshop** folder, and can return to the previous folder when you switch away.

## Actions

- Copy and paste actions between flows with Ctrl/Cmd + C and V.
- Drag actions to reorder them.
- **Run** in the editor runs the actions right away, without pressing the button.
- **Set Accent Color** recolors a slider or history graph. Put it inside an **If / Else** on a value, for
  example to turn a graph red above a limit and back to blue below it. **Reset** returns to your theme color.

## Share and back up

- Export a widget, folder or profile and import it anywhere, or share the file.
- **Settings > Backups** makes backups on demand and automatically before every update. See
  [Backups](/guide/backups/).

## Test automations

**Developer Tools > Trigger event** fires an event for real. Every automation listening to it runs.
