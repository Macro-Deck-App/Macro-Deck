---
title: Connect over USB
description: Connect an Android device over a USB cable instead of Wi-Fi, with the companion app or the web client.
---

An Android device can connect over a USB cable, for example when Wi-Fi is unavailable, blocked or
unstable, with the companion app or with the web client in its browser. This uses ADB, the Android
Debug Bridge. iPhone and iPad connect over your network only.

## On the computer

1. Open **Settings > ADB**.
2. Turn on **Enable ADB**.
3. If **ADB program** shows **Not found**, click **Download platform-tools**. Macro Deck downloads the
   official Android platform-tools from Google, about 15 MB, and uses them for Macro Deck only. An
   existing Android SDK or an `adb` on your `PATH` is found automatically.
4. Turn on **Allow Macro Deck Android connections over USB**.

## On the phone

1. Turn on **Developer options**: open **Settings > About phone** and tap **Build number** seven times.
   The exact place differs a little between manufacturers.
2. In **Settings > Developer options**, turn on **USB debugging**.
3. Connect the phone to the computer with a USB cable.
4. Confirm the dialog **Allow USB debugging?** on the phone. Tick **Always allow from this computer** so
   it does not ask again.

The phone now appears under **Devices** in **Settings > ADB** as **Ready**, and **Android USB
connections** shows **Ready on port …**.

## Connect the app

Open the Macro Deck app on the phone. It finds Macro Deck through the cable by itself. Sign in with
your Macro Deck account, just like over Wi-Fi.

## Use the web client instead

No companion app? Open the browser on the Android device and go to `http://127.0.0.1:` followed by
the port **Android USB connections** shows for the device, for example:

```
http://127.0.0.1:8193
```

`127.0.0.1` is the device itself, and the cable forwards it to Macro Deck on your computer. Sign in as
usual.

## If it does not work

| Shown in Settings > ADB | What to do |
| --- | --- |
| Not authorized - confirm the dialog on your phone | Unlock the phone and confirm **Allow USB debugging?**. If the dialog does not appear, unplug the cable and plug it in again. |
| No permission to access this device | On Linux, your user needs permission for the USB device, usually through a udev rule for Android devices. |
| Offline | Unplug and reconnect the cable, or click **Restart ADB server**. |
| No devices | Use a cable that carries data, not only power, and check that **USB debugging** is on. On Windows, some phones need the manufacturer's USB driver. |

**Restart ADB server** affects other programs that use ADB on this computer, such as Android Studio.

Macro Deck leaves the ADB server running when it exits, unless you turn on **Stop ADB server when
Macro Deck exits** under **Status**. Macro Deck then stops the server when it exits, restarts or
installs an update, but only a server Macro Deck started itself; one that was already running is left
alone. Other programs using that server, such as Android Studio, lose their connection. If you turn
off **Enable ADB** before quitting, the server keeps running.

## Plugins and ADB

Some plugins use Macro Deck's ADB connection to work with your Android devices: they can run commands on
them, copy files and install apps. A plugin can do this only while **Enable ADB** is on and
**Allow plugins to use ADB** is on in **Settings > ADB**, and only if it says in advance that it uses ADB.
**Allow plugins to use ADB** is on by default and applies to every plugin that uses ADB.

- When you install a plugin that uses ADB, the install dialog says so.
- A plugin that uses ADB lists **Uses ADB** under **Capabilities** on its page.
- If you install such a plugin while **Enable ADB** or **Allow plugins to use ADB** is off, Macro Deck
  asks **A plugin wants to use ADB** in a dialog. Click **Allow** to turn on both, so plugins can use ADB.
  **Not now** closes the dialog; the question stays in your notifications until you answer or dismiss it.
- If Macro Deck cannot find adb after you allow it, the dialog offers **Download platform-tools**, the same
  download as in **Settings > ADB**.
- A plugin that tries to use ADB while it is off asks the same question once each time Macro Deck runs,
  even if it was installed earlier. It also asks when ADB is on but Macro Deck cannot find adb, and then
  offers **Download platform-tools** straight away.
- To use a phone over Wi-Fi, turn on wireless debugging on the phone and enter its IP address and port in
  **Settings > ADB**, **Connect over Wi-Fi**, for example `192.168.1.20:5555`. The phone then appears under
  **Devices**, and plugins that use ADB can reach it too. On Android 11 and later the phone must have been
  paired with this computer first. A phone on Wi-Fi reaches Macro Deck over the network, so Macro Deck sets up no USB
  tunnel for it; the device list shows it as **Connected over Wi-Fi**.

Turning off **Allow plugins to use ADB** stops every plugin from using Macro Deck's ADB connection. It
does not affect USB connections for the app or the web client.
