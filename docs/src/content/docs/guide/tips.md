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
- **Drop an image, an app or a shortcut** on a tile to use it as the background.

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

## Share and back up

- Export a widget, folder or profile and import it anywhere, or share the file.
- **Settings > Backups** makes backups on demand and automatically before every update.

## Test automations

**Developer Tools > Trigger event** fires an event for real. Every automation listening to it runs.
