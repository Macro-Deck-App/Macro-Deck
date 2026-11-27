---
title: Clipboard
description: How Macro Deck reads, sets and clears the clipboard of the computer it runs on, copies images and files, reacts to clipboard changes, and what to install on Linux.
---

The Clipboard integration works with the clipboard of the computer Macro Deck runs on. Its actions put text,
an image or files on the clipboard, read the text into a variable, check what is on it and clear it. With
watching turned on, it also tells you what is on the clipboard through variables and runs automations when
the clipboard changes. It works on Windows, macOS and Linux and needs no account or online service.

## Turning it on

Open **Integrations**, choose **Clipboard** and turn it on. The actions work right away, without any setup.

Watching the clipboard is off until you turn it on, because the clipboard often holds things that should
stay private, such as passwords or access tokens you just copied. To turn it on, add the integration's
settings entry with **Set up** in the **Configuration** section of its **Overview**:

| Setting | What it does |
| --- | --- |
| **Watch the clipboard** | Macro Deck notices every change to the clipboard. This turns on the [events](#triggers) and the [variables](#variables). Off by default. |
| **Show clipboard text as a variable** | Macro Deck also puts the text on the clipboard into the `clipboard_text` variable. Only available with **Watch the clipboard** turned on. Off by default. |

Without **Show clipboard text as a variable**, the variables only say what kind of content is on the
clipboard, never the text itself. Turn it on only when you need the text in a widget or a template, since any
password you copy then shows up in that variable while it is on the clipboard.

Changing the settings takes effect at once.

## Actions

| Action | What it does |
| --- | --- |
| **Read clipboard text** | Writes the text on the clipboard into the variable you choose under **Variable**. It fails when the clipboard holds no text. |
| **Copy text** | Puts the **Text** you enter on the clipboard. The text can span several lines and can contain variables and templates, which Macro Deck fills in before it copies the text. |
| **Copy image** | Puts the **Image** file you choose on the clipboard as an image, so it can be pasted into a chat or an image editor. Supported formats are PNG, JPEG, GIF, BMP and TIFF (`png`, `jpg`, `jpeg`, `gif`, `bmp`, `tif` and `tiff`). |
| **Copy files** | Puts files and folders on the clipboard, as if you had copied them in your file manager. Under **Paths**, enter one absolute path per row, or pick a file with **Browse**, and add a row with **+** for each further file or folder (type folder paths in), for example `C:\Users\me\Documents\report.pdf` or `/home/me/report.pdf`. Every path must exist. |
| **Clear clipboard** | Removes everything from the clipboard. |
| **Check clipboard content** | Checks whether the clipboard holds the **Content** you choose (**Text**, **Image**, **Files** or **Any content**) and writes `true` or `false` into the variable you choose under **Variable**. Use it in a script to do something only when, for example, text was copied. |

The actions do not need **Watch the clipboard**.

## Triggers

The Clipboard integration offers three events for automations and widget flows:

| Event | When |
| --- | --- |
| **Clipboard changed** | Something new was copied. The event carries the **Content type** and, for text, the **Text** |
| **Copied text matches** | Text was copied that matches the **Pattern** you enter. The event carries the **Pattern**, the **Text** and the **Match**, the part that matched |
| **Clipboard content type changed** | The kind of content changed, for example from text to an image. The event carries the new **Content type** and the **Previous content type** |

The **Pattern** of **Copied text matches** is a regular expression. For example, `^https?://` reacts
to a copied web address and `\d{6}` to a six-digit code. The match is case-sensitive; start the pattern with
`(?i)` to ignore case, for example `(?i)invoice`.

The events only fire while **Watch the clipboard** is turned on. They never fire for changes Macro Deck made
itself, so a **Copy text** action does not start an automation that reacts to clipboard changes and
cannot set off a loop. Turning on watching or starting Macro Deck fires no event for what is already on the
clipboard.

## Variables

While **Watch the clipboard** is turned on, the integration keeps these variables up to date:

| Variable | Contains |
| --- | --- |
| `clipboard_content_type` | What is on the clipboard: `empty`, `text`, `image`, `files` or `other` |
| `clipboard_has_text` | Whether the clipboard holds text |
| `clipboard_has_image` | Whether the clipboard holds an image |
| `clipboard_has_files` | Whether the clipboard holds files |
| `clipboard_text` | The text on the clipboard. Only with **Show clipboard text as a variable** turned on as well |

Turning off **Show clipboard text as a variable** or **Watch the clipboard** removes `clipboard_text`, so the
last copied text does not stay behind in it.

## Limits

- **Read clipboard text** reads text up to 1 MiB. Larger text makes the action fail.
- The events and the `clipboard_text` variable carry text up to 64 KiB. For larger text, the text is left
  out and only the content type is reported.

## Platforms

### Windows and macOS

The integration works without anything to install.

### Linux

Macro Deck uses a small clipboard tool on Linux: `wl-clipboard` on Wayland and `xclip` on X11. Until the one
for your session is installed, the integration shows the issue **The clipboard is not available**, naming the
missing package. Install it with:

| Distribution | Wayland | X11 |
| --- | --- | --- |
| Debian, Ubuntu | `sudo apt install wl-clipboard` | `sudo apt install xclip` |
| Fedora | `sudo dnf install wl-clipboard` | `sudo dnf install xclip` |
| Arch | `sudo pacman -S wl-clipboard` | `sudo pacman -S xclip` |

Some limits come from the Linux desktop:

- **Watching on Wayland** needs a compositor that supports the data-control protocol, as KDE Plasma, Sway and
  Hyprland do. GNOME does not support it, so there the actions work but watching does not: with **Watch the
  clipboard** turned on, the integration shows the issue **Watching the clipboard is not supported**, and
  the events and variables stay inactive.
- **Images** are put on the clipboard in their original format, not converted. An app that only accepts
  some formats may not paste every image.
- **Copied files** paste in file managers such as Dolphin, but not in GNOME Files.

## Privacy

Macro Deck never logs or saves clipboard content. The text it reads stays in memory and reaches only the
variables, events and actions you set up. With **Show clipboard text as a variable** turned on, the
`clipboard_text` variable holds the copied text as long as it is on the clipboard, and anything that can read
your variables can see it.
