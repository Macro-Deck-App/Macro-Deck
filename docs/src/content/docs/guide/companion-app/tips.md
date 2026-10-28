---
title: Tips and automations
description: Turn a Companion device's screen off when Macro Deck quits or the computer locks, and other ideas with the Companion actions and variables.
---

Every phone or tablet running the Companion app appears in Macro Deck as a device of the **Macro Deck
Companion** integration. Automations and buttons can then act on it, and its state is available as variables.

## What Macro Deck can do with a device

| Action | Android | iPhone, iPad |
| --- | :---: | :---: |
| **Set screen brightness** | ✅ | ✅ |
| **Set orientation**: automatic, portrait or landscape | ✅ | ✅ |
| **Vibrate** | ✅ | iPhone only |
| **Take screenshot** of the deck, into a **Folder** on the computer | ✅ | ✅ |
| **Take screenshot** of the full screen | ✅ | ➖ |
| **Turn screen on** and show this Macro Deck | ✅ | ➖ |
| **Turn screen off** | ✅ | ➖ |
| **Bring to front**: show this Macro Deck's deck | ✅ | ➖ |

On Android, turning the screen on and off and bringing the deck to the front need the permissions under
**Device control** in the app settings; see [Android](/guide/companion-app/android/#device-control). Each action
asks for the device first: choose it from the list, where it appears once it connected.

The device's variables are **Battery level**, **Charging**, **Orientation**, **Screen brightness**, **Model**,
**Platform**, **App version**, **In focus**, **Network type**, **Metered network**, **Internet access**,
**Network name** (Android, needs the Wi-Fi name permission), **CPU usage** (iOS) and **Memory usage**. Use them in
labels, conditions and automations like any other variable. The device's battery also appears in the
[Battery widget](/guide/concepts/#battery), with its own [battery variables](/guide/concepts/#battery-variables).

## Turn the screen off when Macro Deck quits

A tablet that only serves as a deck does not need its screen on while Macro Deck is not running. On Android:

1. In the app, turn on **Turn screen off** (or the **Accessibility service**) under **Device control**.
2. In Macro Deck, create an automation with the event **Macro Deck > Server Stopped**.
3. Add the action **Macro Deck Companion > Turn screen off** and choose the tablet.

![The automation Screen off when Macro Deck quits: the event Server Stopped runs Turn screen off on the tablet](../../../../assets/guide/companion/desktop-automation-server-stopped.png)

Macro Deck waits about two seconds for these actions before it closes its connections, which is enough for
a device that is connected at that moment. It works when Macro Deck quits normally, for example with **Quit** in
the tray menu, but not when Macro Deck is killed or crashes.

## Turn the screen off while the computer is locked

The **System** integration's **Locked** variable (`system_locked`) is `true` while the computer is locked. Two
automations turn the tablet off when you lock the computer and on again when you unlock it:

| Automation | Event | Only run when | Action |
| --- | --- | --- | --- |
| Screen off when the computer locks | **Macro Deck > Variable Changed** | **Variable** `system_locked`, **Changed to** `true` | **Turn screen off** |
| Screen on when the computer unlocks | **Macro Deck > Variable Changed** | **Variable** `system_locked`, **Changed to** `false` | **Turn screen on** |

![The automation Screen off when the computer locks: Variable Changed on system_locked, changed to true](../../../../assets/guide/companion/desktop-automation-computer-locked.png)

**Turn screen on** needs **Background connection** and **Display over other apps** in the app, so the tablet
stays connected while its screen is off. Automations keep running while the computer is locked, even though
buttons on the deck do not.

This works on Windows and macOS only. On Linux the variable has no value, so these automations never run, see
[Buttons still work while a Linux computer is locked](/guide/troubleshooting/#buttons-still-work-while-a-linux-computer-is-locked).

## Turn the screen on when Macro Deck starts

When Macro Deck starts, the tablet is not connected yet, so **Server Started** is too early for it. Use the
moment the app is back instead:

| Automation | Event | Only run when | Action |
| --- | --- | --- | --- |
| Screen on when the tablet is back | **Macro Deck Companion > Device Ready** | **Device** set to the tablet | **Turn screen on** |

**Device Ready** runs each time the app has connected and can take commands, also after a short loss of the
network. An automation on **Macro Deck > Client Connected** works as well: a Companion action there waits a
few seconds for the app to be ready.

On Android, **Turn screen on when connected** in the
[connection settings](/guide/companion-app/deck/#connection-settings) does this without an automation, and
**Turn screen off when disconnected** turns the screen off again when the computer shuts down, which no automation can
do: a computer that is off cannot send **Turn screen off**.

## Plugins and your Companion devices

Some plugins work with the phones and tablets connected through the Companion app: they can read what the
devices report, such as battery, model and network, send them the commands in the table above and take
screenshots. A plugin can do this only while **Allow plugins to use Companion devices** is on under
**Plugin access** in **Settings > Companion App** on the computer, and only if it says in advance that it uses
Companion devices. The setting is off until you turn it on, and applies to every plugin that uses Companion
devices.

- When you install a plugin that uses Companion devices, the install dialog says so.
- A plugin that uses Companion devices lists **Uses Companion devices** on its page.
- If you install such a plugin while the setting is off, Macro Deck asks **A plugin wants to use your
  Companion devices** in a dialog. Click **Allow** to turn the setting on. **Not now** closes the dialog;
  the question stays in your notifications until you answer or dismiss it.
- A plugin that tries to use your devices while the setting is off asks the same question once each time
  Macro Deck runs, even if it was installed earlier.
- A screenshot of the full screen still has to be approved on the device, however the setting is set. A
  screenshot of the deck shows everything on the deck and is not asked for on the device.

Turning off **Allow plugins to use Companion devices** stops every plugin from using your Companion devices.
It does not affect the **Macro Deck Companion** integration's own actions, variables and events, or the
deck itself.

The battery level, name and kind of your Companion devices are not covered by this setting: while the
**Macro Deck Companion** integration is turned on, they are shown in the [Battery widget](/guide/concepts/#battery)
and every plugin can read them, like the batteries plugins report. Turn off the integration under
**Integrations** to hide them.

## More ideas

- **Brightness by time of day:** a **Schedule** event at 22:00 runs **Set screen brightness** at 20%, another one at
  08:00 sets it back. Macro Deck's brightness applies while the deck is open; **Brightness > Follow system** in the
  [connection settings](/guide/companion-app/deck/#connection-settings) hands it back to the device.
- **A deck per orientation:** **Profile Changed** runs **Set orientation**, for example landscape for a gaming
  profile with many buttons and portrait for a music profile.
- **Battery at a glance:** add a **Battery** widget to the deck of your phone and pick the tablet, so you see
  when the tablet on the wall needs charging.
- **Screenshots for sharing:** a button with **Take screenshot** saves the deck as a PNG file on the computer,
  and **Save path to variable** puts the file's path into a variable for the next action.
- **Burn-in:** a deck that is always on shows the same picture for hours. With **Keep screen on**, also turn on
  **Dim when idle** in the app's [settings](/guide/companion-app/settings/#deck), set up a **Screensaver** for the
  device in **Settings > Devices**, and use a dark theme and a lower brightness.
- **Wake the computer:** turn on **Wake this computer when connecting** for a connection, and the app wakes the
  computer with Wake-on-LAN when you open its deck. To wake it without opening the app, use the power button on the
  host widget, a wake tile or shortcut on Android, or the **Wake Computer** action and control on iPhone and iPad;
  see [Wake the computer](/guide/companion-app/deck/#wake-the-computer).
- **Several computers:** open the decks of all your computers once, then switch between them with a three-finger
  swipe.
- **iPhone and iPad:** run Macro Deck scripts from the Shortcuts app, from Siri or from a Shortcuts automation,
  such as an NFC tag on your desk. See [iPhone and iPad](/guide/companion-app/ios/#shortcuts-and-siri).
