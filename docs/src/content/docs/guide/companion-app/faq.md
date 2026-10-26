---
title: FAQ
description: Why the Companion app costs money, what the license covers, and how to use the app without Google Play.
---

## Why does Macro Deck Companion cost money?

Macro Deck is made by one person: me, Manuel. I build it in my free time, next to a full-time job.

What started as a small project has grown into a real platform: Macro Deck for Windows, macOS and Linux, a
plugin ecosystem with its own SDK, a Store and a Creator Portal for plugin authors, and native apps for Android
and iOS. Keeping all of that working takes a lot of time, and it comes with running costs: servers and other
infrastructure, build and test machines, code signing certificates, domains and developer accounts.

The Companion app is how I can keep putting that time into Macro Deck. Macro Deck itself and the web client stay
free and open source, and the web client works on every device with a current browser. The app is an optional
add-on for those who want the best experience on a phone or tablet, and buying it directly supports the
development of everything else.

## Is it a one-time purchase or a subscription?

A one-time purchase. There is no subscription and nothing to renew.

## Do I have to buy the app on both iOS and Android?

No. Buy it once, on either platform. Macro Deck keeps the license on your computer and hands it to every
Companion app that connects to it, whether it runs on Android, iPhone or iPad. So one purchase covers all your
phones and tablets.

If you are signed in to your Macro Deck account in Macro Deck, the license is also saved to your account, and
your other computers signed in to it receive it too. See [License and trial](/guide/companion-app/license/).

## Can I try the app before I buy it?

Yes. Every device can use the app for free for 7 days. Tap **Start 7-day trial** when you first open a deck. See
[The 7-day trial](/guide/companion-app/license/#the-7-day-trial).

## Does the app work with Macro Deck 2?

No. Macro Deck Companion works with Macro Deck 3 only. If you scan the QR code Macro Deck 2 shows, or enter
the address of a computer running Macro Deck 2, the app says that Macro Deck 2 is not supported and adds
nothing, with a link to download Macro Deck 3. A computer you added before shows **Macro Deck 2, not
supported**. Download Macro Deck 3 on the computer, see [Installation](/guide/installation/), then add it again
with its new QR code.

If you bought the earlier Macro Deck 2 app for iPhone or iPad, see
[License and trial](/guide/companion-app/license/#if-you-bought-the-macro-deck-2-app).

## The app says it needs an update

The computer runs a newer Macro Deck, which needs a newer version of the app. When you add the computer or open
its deck, the app says so and names the version it needs, and the computer shows **App update required** in the
list. Nothing is added or opened until you update. Update the app from Google Play or the App Store. The version
without Google Play services is updated by Macro Deck itself, see
[Install the Android app from Macro Deck](/guide/companion-app/install-over-adb/).

## The app says Macro Deck needs an update

The computer runs a Macro Deck that is older than this version of the app works with. When you add the computer
or open its deck, the app says so and names both versions. Update Macro Deck on the computer, see
[Updates](/guide/updates/). The deck keeps trying on its own while Macro Deck restarts and opens as soon as the
computer is updated. For a computer with a Macro Deck from before this check, the list can still show **Online**;
its deck says so when you open it.

## Macro Deck is behind a reverse proxy with HTTPS and the app does not connect

Update the app to the latest version. A connection to a reverse proxy, added by typing its domain and port as
described in [Set up and connect](/guide/companion-app/setup/#enter-the-address), then switches to HTTPS by
itself. This also applies to a connection you added earlier that says it is not a Macro Deck host, or that it is
offline. Turning on **Prefer HTTPS** is
not needed; that setting is for the certificate authority of Macro Deck itself.

If it still does not connect, check in a browser on the same phone or tablet that the address opens Macro Deck
without a certificate warning, and that the proxy passes WebSocket connections through. If the connection
trusts the certificate authority of Macro Deck and the proxy's certificate is refused, the app asks you to review
that certificate authority: check the date and time on the device and the proxy's certificate instead.

## Nothing happens when I tap an NFC tag

Check that the screen is on and, on Android, unlocked, and that NFC is turned on. On iPhone, tap the banner iOS
shows. The tag has to be set up and enabled in the app on this device under **NFC tags**: **Read tag** tells you
whether it is. A tag tapped again within a few seconds does nothing on purpose. See
[NFC tags](/guide/companion-app/nfc-tags/).

## Can I use the app on a device without Google Play services?

Yes. There is a separate version of the Android app that does not need Google Play services, for example for
Amazon Fire tablets. In Macro Deck, open **Settings > Companion App**: it can install this version on a device
connected over ADB, or save the APK so you can install it yourself. See
[Install the Android app from Macro Deck](/guide/companion-app/install-over-adb/).

This version cannot buy the license itself. Buy it once in the app on any Android device with Google Play or on
an iPhone or iPad, and connect that device to your computer. Macro Deck then keeps the license and passes it on
to the device without Google Play as well.

## I have neither Google Play nor the App Store. How can I buy the license?

Right now, the license can only be bought through Google Play or the App Store. Another way to buy it may come
later. Until then, the [web client](/guide/compatibility/#devices-you-control-it-from) works on any device with a
current browser, with all your decks, free of charge.
