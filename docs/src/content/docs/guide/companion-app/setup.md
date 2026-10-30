---
title: Set up and connect
description: Add your computer to the Companion app with a QR code, from the network, by address or over USB, and sign in.
---

The app keeps a list of your computers under **Connections**. Add each computer running Macro Deck once;
after that, tap it to open its deck.

![The Connections list on Android: the trial banner and two saved computers, both connected](../../../../assets/guide/companion/android-connections.png)

## First start

A new installation opens with three short steps before the Connections list:

1. **Welcome to Macro Deck**: what the app is, with a note that Macro Deck 3 is in beta and links to report bugs
   on Discord or GitHub.
2. **Connect to your computer**: the app needs Macro Deck installed and running on your computer. **Get Macro
   Deck** opens [macro-deck.app](https://macro-deck.app) if you do not have it yet.
3. **Support Macro Deck's development**: Macro Deck itself and its web client are free and open source; the
   optional Companion app is a one-time purchase after a free 7-day trial, with no automatic charges.

**Next** and **Back** move between the steps, and Android's back gesture goes back one step too. If you close the
app halfway, it opens on the step you left. On the last step:

- **Start 7-day free trial** finishes and asks for the trial. The 7 days begin when you first connect to a
  computer; see [The 7-day trial](/guide/companion-app/license/#the-7-day-trial).
- **More information** opens the purchase screen with what the app adds over the web client and the price in your
  store. Closing it returns to the last step; nothing is started or bought until you tap **Buy license** there.
- **How the license works** opens [License and trial](/guide/companion-app/license/) in the browser.
- **Continue without trial** finishes without starting the trial. You can still start it later from the paywall.

![The last onboarding step on Android: Try it free for 7 days, No automatic charges, the How the license works link, and the Start 7-day free trial, More information and Continue without trial buttons](../../../../assets/guide/companion/android-onboarding.png)

The steps show only once, on a new installation. If you updated from an earlier version, or the device already
holds a license or a trial (on iPhone and iPad these survive reinstalling the app), the app opens on the
Connections list as before.

The app asks for no permissions up front. On iPhone and iPad it asks for access to the local network the first
time it looks for computers, after the first start, and for the camera the first time you scan a code.

## Add a computer

Tap **Add connection** (on iPhone and iPad, **+**). There are four ways:

### Scan the QR code

The quickest way, and the one that needs no password.

1. In Macro Deck on the computer, open the network panel with the network icon at the top right. It shows a
   QR code, **Scan with the Macro Deck app**.
2. In the app, choose **Scan QR code** and point the camera at the code. The phone's own camera app works
   too: the code opens the Macro Deck app.

The code carries the computer's name, its addresses, a short-lived pairing code and its identity, so the app
signs in by itself and needs no identity check. A pairing code is valid for 15 minutes; if the app says
**That code has expired**, open the panel again for a new code. When Macro Deck offers both, the app asks
whether to connect over HTTPS or HTTP; choose **HTTPS (recommended)**.

A device without a camera, such as some desk displays, cannot scan. Enter the address instead.

The QR code of Macro Deck 2 does not work: the app says Macro Deck 2 is not supported. Update Macro Deck on the
computer to Macro Deck 3 first; see [Does the app work with Macro Deck 2?](/guide/companion-app/faq/#does-the-app-work-with-macro-deck-2).

### Pick it from the network

Computers that show themselves on the network appear under **Found on this network** on the Connections
screen. Tap one to add it. This needs **Show on the local network** turned on in **Settings > Network** in
Macro Deck, and **Find hosts on the network** in the app's settings. Networks that isolate their devices,
such as many guest networks, hide the computer.

### Enter the address

Choose **Enter address** and type the computer's address, the port and a name for the connection.
**Settings > Network** in Macro Deck and the network panel list the addresses; the port is the **Listening
port** there. Tap **Continue**.

![Adding a connection by address on Android: host 192.168.1.20, port 8391 and the name Streaming Computer](../../../../assets/guide/companion/android-enter-address.png)

If Macro Deck is behind a reverse proxy with HTTPS, such as nginx, Caddy or Traefik with a Let's Encrypt
certificate, type the proxy's domain without `https://` and its port, usually 443. The app notices that the
address only answers over HTTPS and uses HTTPS for it by itself; **Prefer HTTPS** is not needed for that. The
certificate has to be one the phone or tablet already trusts, issued for that domain, and the proxy has to pass
WebSocket connections through. Android 7.1 and older may not trust Let's Encrypt certificates.

When the proxy runs on the same computer as Macro Deck, turn on **Behind a reverse proxy on this computer** in
**Settings > Network** in Macro Deck, and let the proxy send the `X-Forwarded-For` and `X-Forwarded-Proto`
headers. Caddy and Traefik send them by default; nginx needs
`proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;` and `proxy_set_header X-Forwarded-Proto $scheme;`.
Without the setting, Macro Deck sees every device behind the proxy as the computer itself: a few wrong passwords
from one of them pause sign-in for all of them, and the sign-in notification shows the computer's own address.
Plugins cannot pair or connect through a proxy.

### Plug in a USB cable

A phone or tablet can also reach Macro Deck through a USB cable, with or without USB debugging. The app then
shows a **USB connection** row by itself. See [Connect over USB](/guide/usb-connection/).

## Sign in

A computer added by address or from the network asks you to sign in. The sign-in form opens on
**Pairing code**:

1. In Macro Deck on the computer, open the network panel with the network icon at the top right. It shows
   the pairing code next to the QR code.
2. Type the six digits into the boxes. The app signs in as soon as the sixth digit is in; **Sign in** is
   there for another try. Pasting the code, or the code your keyboard suggests, works too.

A code is valid for 15 minutes and works once. If the app says **That code is wrong or has expired**, check
the code in the network panel and type it again. After five wrong codes Macro Deck stops accepting codes for
a while and shows a new one; the app says **Too many failed attempts**, so wait a moment before trying again.

To sign in with your account instead, choose **Password** and enter the user name and password you created in
Macro Deck on the computer. If you forgot the password, reset it on the computer under
**Settings > Security**.

![The sign-in sheet in the Android app, with the pairing code boxes and the hint where to find the code](../../../../assets/guide/companion/android-sign-in.png)

![The sign-in sheet on iPhone, with the choice between Pairing Code and Password](../../../../assets/guide/companion/ios-sign-in.png)

A device signed out in Macro Deck under **Settings > Devices** shows **Sign in again**.

## Check the identity

Every Macro Deck has an identity: six groups of four characters, shown as **Identity** in the network panel and
in **Settings > Network**. When the app cannot tie the computer to a scanned code, for example for an address you
typed, a computer found on the network or a new computer on the cable, it shows the identity it received and
asks you to compare it. Tap **Fingerprints match** only if all six groups are the same as on the computer.

The identity protects you from signing in to a different machine that pretends to be yours. The app keeps it
and warns you if it ever changes. After reinstalling Macro Deck the identity is new: the app asks whether to
replace the one it knew, and Macro Deck asks you to scan its QR code again on every paired device.

If you add a computer that is already in the list under another address, the app asks **Same computer?** and
offers to add the new address to the existing connection.

## Open the deck

Tap the computer. Without a license or a running trial, the app shows the paywall: start the free 7-day trial,
buy the license, or tap **Open web client** to use this computer's free web client in your browser instead.
See [License and trial](/guide/companion-app/license/). Once your computer has a license, every device that
connects to it gets it, and the deck opens straight away.

Which profile a device opens with, and its screensaver, are set per device in Macro Deck under
**Settings > Devices**.

## If it does not connect

When a computer cannot be reached, the app shows **Cannot reach this computer** and goes through the usual
causes one at a time: both devices in the same network, Macro Deck running, the firewall, Wi-Fi isolation and
VPNs. See also [Troubleshooting](/guide/troubleshooting/).
