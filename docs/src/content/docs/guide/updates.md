---
title: Updates
description: How Macro Deck checks for, downloads and installs updates, and what changed after one.
---

Choose how Macro Deck handles new versions in **Settings > About**, under **Updates > Automatic updates**.
**Check for updates** in the same place works in every mode.

| Mode | What happens when a new version is out |
| --- | --- |
| **Off** | Nothing. Macro Deck only looks for updates when you check yourself. |
| **Notify only** | Macro Deck tells you and asks before it downloads and installs anything. This is the default. |
| **Automatic** | Macro Deck downloads the update in the background and installs it on its own. Windows and macOS only. |

## Automatic installs

Once an automatic download finishes, a dialog counts down 60 seconds before Macro Deck restarts to install
the update.

- **Install now** restarts right away.
- **Not now**, or closing the dialog, skips the install until you next start Macro Deck. You can still
  install it from **Settings > About** in the meantime.

The countdown runs even when the Macro Deck window is closed, so an unattended computer stays up to date.
Switching the mode away from **Automatic** during the countdown stops it.

With **Before Macro Deck updates** turned on in **Settings > Backups**, Macro Deck makes a backup before it
installs.

## What's new

The first time Macro Deck starts after an update installed from within the app, it shows the release notes
of the new version once. An update installed by running a downloaded installer yourself shows nothing.

## Linux

On Linux, Macro Deck never installs updates itself. It tells you that a new version exists, and you update
through `apt` or the package you installed. See [Installation](/guide/installation/).
