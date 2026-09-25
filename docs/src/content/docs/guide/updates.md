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
| **Automatic** | Macro Deck downloads the update in the background and installs it when you quit Macro Deck. Windows and macOS only. |

## Automatic installs

An automatic download finishes quietly: no dialog opens and nothing restarts while you work. The next
time you quit Macro Deck, it installs the update before it closes. The new version runs from the next
start, which shows what's new.

Quit with **Quit** in the tray icon's menu, or on macOS with **Macro Deck > Quit Macro Deck** (Cmd+Q).
Quitting takes a little longer then: Macro Deck makes the backup set in **Settings > Backups** and installs
the update, and on Windows the installer's progress window shows briefly. If the backup takes longer than
two minutes, Macro Deck quits without installing and downloads the update again at the next start. If
that happens every time, install the update with **Install now** instead.

The update is not installed when:

- you only close the Macro Deck window: Macro Deck keeps running in the background and the update keeps
  waiting;
- you quit from the macOS Dock, log out, or shut down the computer while Macro Deck is running. The
  download is then repeated after the next start.

To install it sooner, open **View details** in **Settings > About** and choose **Install now**.

Switching the mode away from **Automatic** keeps the downloaded update until Macro Deck quits, but no
longer installs it on quit.

## When the Macro Deck window is closed

Macro Deck keeps checking for updates while it runs in the background, for example after it started with
your computer or after you closed its window. When it finds a new version then, it opens a small update
window with the release notes, so you do not have to open Macro Deck to see it.

- In **Notify only**, choose **Download & install**. The window shows the download, then Macro Deck
  restarts to install the update.
- In **Automatic**, the update window does not open. The update downloads in the background and installs
  when you quit Macro Deck.
- **Later**, or closing the window, leaves the update for now.

The update window opens on its own at most once per version while Macro Deck runs, when an update is found
in the background. It never opens in **Off** or **Automatic** mode.

While an update is waiting, the Macro Deck icon in the system tray or menu bar has an **Update to**
entry with the new version. It opens the update window again.

With **Before Macro Deck updates** turned on in **Settings > Backups**, Macro Deck makes a backup before it
installs. See [Backups](/guide/backups/).

## Extension updates

Plugins and icon packs from the Store have their own updates, set in **Settings > Extensions**.

- **Notify about updates** shows a notification when a new version of an installed plugin or icon pack
  is out. It is on by default. Macro Deck tells you about each version once per session; a notification
  you dismiss comes back only when a newer version appears. Opening it shows **Installed** in the Store.
- **Update extensions automatically** installs new versions on its own, then shows one notification
  listing what was updated, or what failed. It is off by default. An automatic update that failed is not
  retried until Macro Deck restarts; update it from **Installed** instead.

If you install an older version of a plugin or icon pack on purpose, or any version other than the latest,
Macro Deck keeps it: automatic updates and update notifications skip that item until you update it yourself,
from its page, its card or **Update all**. **Installed** still shows the newer version as available.

Macro Deck never offers or installs an update to a version the Store has withdrawn. If the version you have
installed is withdrawn, a warning notification says so, whether or not **Notify about updates** is on; see
[Withdrawn versions](/guide/concepts/#withdrawn-versions).

A failed install or update offers **Retry** only when trying again can help, for example after a download
failed. When the item needs a newer Macro Deck, the Store offers **Check for updates** instead; when it does not
run on your platform or version, it says so without a button to try again.

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
version. An update or reinstall also sends the version you had installed. Replacing an installed version with an older
one counts as an update. Each install or update action gets
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
