---
title: Troubleshooting
description: Fixes for common problems when installing, connecting and using Macro Deck.
---

Find your symptom below. If nothing here helps, ask on [Discord](https://discord.macro-deck.app) or
[open an issue on GitHub](https://github.com/Macro-Deck-App/Macro-Deck/issues) and attach your logs.

## Where to find the logs

Macro Deck writes its logs to the `logs` folder inside its data folder:

| System | Data folder |
| --- | --- |
| Windows | `%APPDATA%\MacroDeck` |
| macOS | `~/Library/Application Support/MacroDeck` |
| Linux | `~/.local/share/MacroDeck`, or `$XDG_DATA_HOME/MacroDeck` if that is set |

## A device cannot connect

- **Same network:** your phone, tablet or browser must be on the same network as the computer
  running Macro Deck. A phone on mobile data cannot reach it.
- **Client isolation:** many routers can stop devices on the same Wi-Fi from reaching each other,
  often called AP isolation, client isolation or wireless isolation. Guest networks and public
  Wi-Fi almost always have it on. Connect both devices to the main network, or turn the setting
  off in your router.
- **Different subnets:** a mesh system, a second router or separate networks for 2.4 GHz and 5 GHz
  can put the two devices in different subnets that do not route to each other. Compare the first
  parts of both devices' IP addresses, for example `192.168.1.x` and `192.168.0.x`.
- **VPN:** a VPN on either device can send local traffic through the tunnel instead of your
  network. Disconnect it, or allow local network access in the VPN app.
- **Local network permission:** on iPhone and iPad, apps and some browsers such as Chrome need the
  Local Network permission, under **Settings > Privacy & Security > Local Network**.
- **Port:** Macro Deck listens on port `8193` by default, and on `8194` for HTTPS. You can see the
  port it is actually using in **Settings > Network**.
- **Firewall:** allow Macro Deck, or the port above, through the firewall of the computer running it.
- **No Wi-Fi at all:** an Android phone can connect over a USB cable instead, see
  [Connect over USB](/guide/usb-connection/).

## Macro Deck is not listening on its port

If another program already uses the port, Macro Deck starts without it and devices cannot connect.
**Settings > Network** then says the port could not be used. Choose a different port there and
restart Macro Deck.

## Buttons do nothing and devices show a lock screen

Macro Deck does not run actions while the computer it runs on is locked. Unlock the computer and try
again.

Connected devices show a lock screen for as long as the computer is locked. You can keep the deck
visible instead by turning off **Lock clients when this computer is locked** in
**Settings > Security**. Actions stay blocked either way.

## Macro Deck does not open a second time

Only one Macro Deck can run on a computer at a time. If it is already running, starting it again
does not open a second copy. Look for the running Macro Deck in the system tray or menu bar.

## Plugins show up as dotnet in Task Manager

Most plugins run on the .NET runtime that comes with Macro Deck, so Task Manager and Activity Monitor
list them as `dotnet` or ".NET Host" rather than by the plugin's name. This is expected; ending
Macro Deck ends them too.

## Installing on Linux

- **The stable APT suite is empty:** there is no stable release of Macro Deck 3 yet. Use the beta
  suite as described in [Installation](/guide/installation/#debian-and-ubuntu).
- **Macro Deck is not in the AUR:** that is expected for now. Use the RPM package or the AppImage.
