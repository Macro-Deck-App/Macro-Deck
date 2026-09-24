---
title: Connect over USB
description: Connect a phone or tablet over a USB cable instead of Wi-Fi, with or without USB debugging.
---

A phone or tablet can connect over a USB cable, for example when Wi-Fi is unavailable, blocked or
unstable. There are two ways:

- [Without USB debugging](#without-usb-debugging), experimental: Android phones and tablets, iPhone and iPad with the
  Macro Deck app. Nothing needs to be turned on on the phone, but you turn it on in Macro Deck first.
- With ADB, the Android Debug Bridge: Android devices with the companion app or with the web client in
  the browser. This needs USB debugging on the phone, and is described first below.

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

## Without USB debugging

The Macro Deck app can also connect over the cable without USB debugging and without ADB, for Android
phones and for iPhone and iPad. This is experimental: it is off until you turn it on, and not every phone
supports it yet. If yours does not, ADB and Wi-Fi keep working as before.

### On the computer

1. Open **Settings > USB connections**.
2. Turn on **USB without debugging**. It is marked **Experimental**.
3. **Status** shows whether this computer can do it for **Android devices**, which need libusb, and for
   **iPhone and iPad**, which need the Apple device service (usbmuxd). See
   [what the computer needs](#what-the-computer-needs).

### Android phone

1. Install the Macro Deck app and connect the phone with a USB cable that carries data.
2. The phone appears under **Devices** in **Settings > USB connections**. Click **Connect without
   debugging** next to it.
3. The phone switches into accessory mode and asks whether to open Macro Deck for this computer, named as
   in Macro Deck's connection info. Unlock the phone if the question does not show. Tick the option to
   always open it, and confirm. The Macro Deck app opens.
4. On a Mac with Apple silicon, the first time you do this macOS asks **Allow accessory to connect?**. Click
   **Allow**, then click **Connect without debugging** again; the phone does not need to be plugged in again,
   although that works too. Sign in, and compare the
   fingerprint the app shows with the one in Macro Deck the first time.

Macro Deck remembers a phone once it connected, and connects it again by itself the next time you plug it
in. **Remembered devices** lists them; **Forget** stops that. Nothing is ever sent to a phone you did not
choose here.

While the phone is connected this way:

- It offers no file transfer (MTP) until you unplug it.
- Without the Macro Deck app installed, the phone asks which app should open. Install the app, or unplug
  the phone.
- ADB sessions of other programs on this phone end when it switches, for example Android Studio.
- If you turn off USB detection in the app, Macro Deck does not connect the phone again until you unplug it
  and plug it in again.

When **Enable ADB** is on in **Settings > ADB**, a phone ADB already knows is shown as **Connected through
ADB** and stays with ADB until you click **Connect without debugging** for it. Once you do, or once Macro Deck
remembers the phone, USB without debugging takes over: the phone switches even though ADB knows it, and
Macro Deck sets up no ADB connection for it while it is switched. Other programs' ADB sessions to that phone,
such as scrcpy or Android Studio, end when it switches, and the list says so next to the button. Phones you
did not choose are left to ADB. If the phone falls back to ADB later, for example after you turn **USB without
debugging** off or the phone leaves accessory mode, the ADB connection comes back by itself. **Forget** only
stops Macro Deck from switching the phone the next time; a phone that is connected right now stays connected
until you unplug it. A phone that stays in accessory mode without a connection, because USB detection was
turned off in the app (**Closed in the app**) or the Macro Deck app is not installed, can use neither ADB nor
USB without debugging until you unplug it or turn **USB without debugging** off.

### iPhone and iPad

1. Connect the iPhone or iPad with a USB cable and tap **Trust** when it asks whether to trust this
   computer.
2. Open the Macro Deck app and keep it in front. It connects by itself and appears under **Devices** as
   **Connected**. Until the app is open, it shows **Waiting for the app**.

The connection lasts while the app is open. When the app goes to the background it disconnects, and it
connects again when you bring it back.

### What the computer needs

| Computer | Android devices | iPhone and iPad |
| --- | --- | --- |
| macOS | libusb, for example `brew install libusb` | Nothing extra |
| Windows | libusb, and the WinUSB driver for the phone, both in normal mode and after it switched (for example installed with Zadig) | The Apple Devices app, or iTunes |
| Linux | libusb (`libusb-1.0-0`) and a udev rule that gives your user access to the phone | The `usbmuxd` service |

Macro Deck does not include libusb yet; without it, only **Android devices** shows as not available. After you install
libusb, Macro Deck finds it within about 30 seconds, without a restart.

### Limits

- The app talks plain HTTP over the cable. If Macro Deck only accepts HTTPS, **Status** says so: set
  **Listener mode** in **Settings > Network** to **Use additional HTTPS port**, or turn HTTPS off.

| Shown for a phone | What to do |
| --- | --- |
| Not connected | Click **Connect without debugging** to connect the phone. |
| Switching | Nothing: the phone is switching to accessory mode, which takes a few seconds. |
| Waiting to connect | Nothing: Macro Deck connects the phone within a few seconds. |
| Waiting for the Macro Deck app | Open the app on the phone, or install it. |
| Connected | The deck is ready in the app. |
| Waiting for the app | On an iPhone or iPad: open Macro Deck and keep it in front. |
| Stopped | Click **Connect without debugging** to try again, or unplug the phone and plug it in again. |
| Closed in the app | USB detection was turned off in the app. Unplug and plug in again. |
| Connected through ADB | The phone uses the ADB connection above. Click **Connect without debugging** to use this instead. |
| Not supported by this device | The phone cannot switch to accessory mode. Use ADB or Wi-Fi. |
