---
title: Compatibility
description: Operating systems, devices and browsers Macro Deck supports.
---

## Computer running Macro Deck

| Operating system | Architecture | Packages |
| --- | --- | --- |
| Windows | x64 | Installer (`.exe`) |
| macOS 11 or later | Apple silicon | App |
| Debian, Ubuntu | x64 | APT repository, `.deb` |
| Other Linux distributions | x64 | `.rpm`, AppImage |

See [Installation](/guide/installation/) for how to install each one.

## Devices you control it from

There are two ways to use a device as your deck:

- **Web client:** opens in a browser on your network, with nothing to install. It also works in
  browser-based game overlays. With HTTPS enabled, you can install it as an app (PWA).
- **Companion app:** the native app for Android 6 or later and iOS 15 or later.

Looking for specific devices? See [Recommended devices](/guide/recommended-devices/).

🟢 works well · 🟡 works, with limitations · 🔴 does not work

| OS | Web client | Companion app | Notes |
| --- | :---: | :---: | --- |
| Android 6 or later | 🟢 | 🟢 | |
| Android 4.4 to 5.1 | 🟡 | 🔴 | Some visual effects are missing. |
| Android 4.3 or earlier | 🔴 | 🔴 | The built-in Android browser cannot keep a connection to Macro Deck. |
| iOS 16 or later | 🟢 | 🟢 | |
| iOS 15 | 🟢 | 🟡 | Fine if you already have one. Do not buy one for Macro Deck: the app keeps supporting iOS 15 only as long as Kotlin Multiplatform does and it can still be built for iOS 15 on a current Mac. |
| iOS 9 to 14 | 🟡 | 🔴 | Some visual effects are missing, and on older versions dates and times show as digits. |
| iOS 8 or earlier | 🔴 | 🔴 | |
| Windows, macOS, Linux | 🟢 | | Any current browser. |

## Limitations

### The web client is slower than the companion app

The companion app draws the deck natively: the operating system paints each widget directly. The
web client has to build every widget out of web page elements, and the browser then works out
styles, layout and painting for all of them whenever something changes. On a fast computer you
will hardly notice, but on an older phone or tablet the web client reacts noticeably slower and
uses more battery.

If the web client feels sluggish, set **Rendering** to **Simple** in its settings. That drops
shadows, animations and the history graph, and hides the forecast bars. On devices that support
it, the companion app is the better choice.

### HTTPS is needed for some web client features

Installing the web client as an app (PWA) and **Keep display awake** only work when the web client
is opened at its `https://` address. Turn on HTTPS in **Settings > Network**. Macro Deck signs its
certificate with its own authority, so install and trust that authority once on every device you
use.

### Keeping the display awake

**Keep display awake** needs a browser that supports it. Many older browsers do not, and the
setting then says so. Even where it is supported, battery saver can refuse it, and the browser
pauses it while the screen is off.

### USB connections

Android devices can connect over USB with ADB, with the companion app or with the web client at
`http://127.0.0.1` in the device's browser. ADB is turned on in **Settings > ADB** and uses the official
Android platform-tools from Google, about 15 MB. With the Macro Deck app, Android phones and tablets, iPhone and iPad
can also connect over USB without debugging, set up in **Settings > USB connections**; the web client
cannot. See [Connect over USB](/guide/usb-connection/) for the full setup.

### Older browsers

On the older systems marked 🟡 above, the web client runs in a reduced mode: some visual effects,
such as animated widget borders, are missing. Up to iOS 12, clocks use the device's time zone, and
dates and times are shown as digits.

### Computer requirements

Macro Deck is built for x64 Windows and Linux and for Apple silicon Macs. There are no builds for
Intel Macs or for ARM versions of Windows and Linux. Actions do not run while the computer is locked, see
[Troubleshooting](/guide/troubleshooting/#buttons-do-nothing-and-devices-show-a-lock-screen).
