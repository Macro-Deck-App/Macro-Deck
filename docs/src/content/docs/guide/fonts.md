---
title: Fonts
description: Import your own TTF or OTF fonts for button labels and text, and what happens when you export, back up or remove them.
---

Labels and text widgets can use the fonts installed on your computer. To use a font that is not
installed, for example a dot-matrix font for a clock, a themed alphabet or a high-legibility font,
import it under **Library > Fonts**.

## Import a font

1. Open **Library > Fonts**.
2. Choose **Import fonts** and pick one or more `.ttf` or `.otf` files, or drop them onto the page.

Each imported font is listed with a sample in its own typeface. It then appears in the font pickers
alongside the installed fonts, for example in a button's label settings, where you can type to search the
list and every font is shown in its own typeface. It is drawn on every connected
device: the Macro Deck app, the web client and the Companion app. Hardware decks added by plugins, such as
Stream Deck devices, draw labels in the plugin's own font.

Only TTF and OTF files of up to 32 MB are accepted, one font per file. WOFF and WOFF2 files and font
collections (`.ttc`) are not supported; convert them to TTF or OTF first.

A font is refused when:

- its family is already installed on this computer. A family stays either installed or imported, so a
  missing weight of an installed family, for example Inter Black next to an installed Inter, can't be
  imported either;
- a font with the same family and style is already imported. To replace it, remove the old one first.

Imported fonts are only for labels and text on your deck. They are not offered as the app font in
**Settings > Appearance**.

:::note[Font licences]
Only import fonts whose licence allows this use. Fonts used on your buttons are included when you
export a profile, folder or widgets, so sharing such an export also shares the font.
:::

## Remove a font

Choose the remove button next to a font and confirm. Buttons that use it switch to the default font. The
Macro Deck app does that right away; the web client and the Companion app after they reload or reconnect.

If you later install a font family on your computer that you also imported, the installed one takes over
the next time Macro Deck starts.

## Export, import and backups

When you export a profile, folder or widgets, the imported fonts they use travel inside the file. Importing
it on another Macro Deck installs those fonts there. A font whose family is installed on that computer is
not imported again; buttons use the installed one when it has the same style, and the default font
otherwise. A font that is missing for any other reason also falls back to the default font.

Fonts stay installed when you delete the profile or uninstall the Store item they came with. Remove them
under **Library > Fonts**.

Backups include imported fonts in the **Icons, icon packs and fonts** group. See [Backups](/guide/backups/).
