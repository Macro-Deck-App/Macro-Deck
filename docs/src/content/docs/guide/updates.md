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

Once an automatic download finishes while the Macro Deck window is open, a dialog counts down 60 seconds
before Macro Deck restarts to install the update. A minimized window counts as open.

- **Install now** restarts right away.
- **Not now**, or closing the dialog, skips the install until you next start Macro Deck. You can still
  install it from **Settings > About** in the meantime.

Switching the mode away from **Automatic** during the countdown stops it. Closing the Macro Deck window
during the countdown stops it too, and the update window below asks instead.

## When the Macro Deck window is closed

Macro Deck keeps checking for updates while it runs in the background, for example after it started with
your computer or after you closed its window. When it finds a new version then, it opens a small update
window with the release notes, so you do not have to open Macro Deck to see it.

- In **Notify only**, choose **Download & install**. The window shows the download, then Macro Deck
  restarts to install the update.
- In **Automatic**, the update is already downloaded. Choose **Restart now** to install it. There is no
  countdown: nothing is installed until you choose to.
- **Later**, or closing the window, leaves the update for now.

The update window opens on its own at most once per version while Macro Deck runs, when an update is found
in the background. It never opens in **Off** mode.

While an update is waiting, the Macro Deck icon in the system tray or menu bar has an **Update to**
entry with the new version. It opens the update window again.

With **Before Macro Deck updates** turned on in **Settings > Backups**, Macro Deck makes a backup before it
installs.

## Extension updates

Plugins and icon packs from the Store have their own updates, set in **Settings > Extensions**.

- **Notify about updates** shows a notification when a new version of an installed plugin or icon pack
  is out. It is on by default. Macro Deck tells you about each version once per session; a notification
  you dismiss comes back only when a newer version appears. Opening it shows **Installed** in the Store.
- **Update extensions automatically** installs new versions on its own, then shows one notification
  listing what was updated, or what failed. It is off by default. An automatic update that failed is not
  retried until Macro Deck restarts; update it from **Installed** instead.

Automatic updates only replace plugins and icon packs you installed from the Store. A plugin you
installed from a file, or one installed from the Store before this setting existed, is announced instead;
update it once from the Store to include it. Profile templates are never updated automatically.

With **Before plugin updates** turned on in **Settings > Backups**, Macro Deck makes one backup before a
batch of plugin updates. The Store shows this as its own **Backing up** step before the update installs, so
an update takes longer than a first-time install of the same plugin. Turn the setting off if you would
rather not wait for it.

## Store download statistics

When Macro Deck downloads a plugin, icon pack or profile template from the Store, for a first install, an
update or a reinstall, it tells the Store which of the three it is, so downloads can be counted per
version. An update or reinstall also sends the version you had installed. Each install or update action gets
its own temporary ID, which a retry of that action reuses so it is counted once.

The download sends no account, device or installation ID, and no list of your other extensions. Test builds
and plugins installed from a URL or file send none of this.

## What's new

The first time Macro Deck starts after an update installed from within the app, it shows the release notes
of the new version once. An update installed by running a downloaded installer yourself shows nothing.

## Release notes

Macro Deck loads the release notes from the version's
[GitHub release](https://github.com/Macro-Deck-App/Macro-Deck/releases) when it finds an update. If GitHub cannot
be reached, for example behind a firewall or a proxy that inspects secure connections, the update dialog, the
update window and **What's new** show **Read the release notes on GitHub** instead, which opens the release in
your browser.

## Linux

On Linux, Macro Deck never installs updates itself. It tells you that a new version exists, and you update
through `apt` or the package you installed. The update window and the tray entry still show the release
notes, with the `apt` command or a link to the download page instead of an install button. See [Installation](/guide/installation/).
