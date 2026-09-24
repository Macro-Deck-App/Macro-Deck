# ADR 0095: USB connections without debugging

Status: Proposed

## Context

[ADR 0030](0030-android-usb-connections-over-adb.md) connects Android phones over USB through `adb reverse`,
which needs USB debugging, a developer setting most users should not have to turn on, and an adb install.
iPhone and iPad had no USB path at all. Issues
[Macro-Deck#925](https://github.com/Macro-Deck-App/Macro-Deck/issues/925) and Macro-Deck-Companion#68 ask
for a cable connection that works with the phone as it ships.

Both platforms offer a way to move bytes over the cable without debugging:

- Android Open Accessory (AOA): the computer, acting as USB host, asks the phone to re-enumerate as an
  accessory. An app that declares the matching accessory filter then gets a pair of bulk endpoints.
- usbmuxd: the Apple service every Mac, the Apple Devices app on Windows and the Linux daemon run for
  trusted iPhones. It opens TCP connections to ports on the phone's loopback over the cable. The phone
  side needs nothing but a listening socket (the PeerTalk pattern).

Everything above the socket already exists: the app probes `127.0.0.1:8193/8194`, proves the host
identity ([ADR 0086](0086-host-identity-key.md)), signs in and opens sessions exactly as over adb.

## Decision

### One link protocol over two carriers

The app keeps serving `127.0.0.1:8193/8194` on the phone, but only while a link is up, and carries each
TCP connection it accepts over a multiplexed link to the host. The carrier is the AOA bulk pipe on
Android and a usbmuxd connection to port 8197 on iOS (outside the adb candidates 8193 to 8196). Both run
the same protocol, implemented in `host/src/MacroDeckHost.Infrastructure/Usb/Native/` and mirrored by hand
in the Companion app's `docs/integration/macro-deck-host.md`.

Link protocol v1, all integers big endian:

- Frame: type u8, flags u8, stream u16, length u32, payload. A frame is at most 16384 bytes, so the
  payload is at most 16376; a larger length is malformed. Types: HELLO 0, OPEN 1, DATA 2, CLOSE 3,
  WINDOW 4, BYE 5. A single 0xFF byte at a frame boundary is filler and skipped.
- HELLO on stream 0: magic `MDLK`, max version u8, epoch u32, echo u32 (13 bytes; readers accept a longer
  payload and ignore the rest). Flags bit 0 is ack. Each side picks a random non-zero epoch whenever it
  (re)starts, which also resets its own streams, and resends HELLO(ack 0, echo 0) every second until a
  HELLO(ack 1) echoes it. A HELLO with an epoch other than the recorded peer epoch resets every stream
  and records it; a repeated one changes nothing; every HELLO(ack 0) is answered with HELLO(ack 1) echoing
  the sender's epoch. The app opens streams only once its epoch is answered and a peer epoch is recorded.
  The negotiated version is the lower maximum.
- One restart routine serves every loss: malformed input, a frame still incomplete two seconds after its
  first byte, fifteen seconds without receiving anything, a frame not written within fifteen seconds, and
  a carrier that was (re)opened. It picks a new epoch, drops the streams, rescans what is buffered for a
  HELLO and says HELLO again. After five seconds without sending, a side sends WINDOW on stream 0 with
  credit 0 as keepalive.
- OPEN (app to host) starts a stream; the host dials its own public listener. CLOSE ends both directions.
  WINDOW grants additional credit; each stream starts with 256 KiB per direction and the receiver grants
  back what it wrote to its local socket. BYE (app to host) is an intentional close.
- Over AOA every frame is one USB write. A frame whose length is a non-zero multiple of 64 below 16384 gets
  one filler byte, so the transfer ends on a short packet and no zero-length packet is needed; a zero-length
  read means no data. A transfer times out after five seconds and continues with what is left, within the
  fifteen seconds a frame has.
- The accessory strings are manufacturer `Macro Deck`, model `Macro Deck Companion`, version `1`, URI
  `https://macro-deck.app` and an empty serial. The app's accessory filter and Android's "always open"
  choice match manufacturer and model only. The description is this computer's name as mDNS and the
  connection info show it, trimmed and cut to 64 characters and 200 UTF-8 bytes (Android keeps each
  string in 256 bytes) without splitting a character, or `Macro Deck` when there is none, so
  Android's prompt names the computer instead of repeating the app's name.

Frames carry no integrity check; the cable and USB CRC are trusted. A frame torn by a write timeout can
deliver a few garbage bytes to one HTTP stream before the reader resyncs, which that request survives as a
failed request, not as a broken link.

### The host dials its public listener, and the connection is stamped

A bridged connection lands on the public plain HTTP listener (`PublicEndpointSet.LocalClientEndpoint`),
never on the loopback listener, for the reason ADR 0030 gives. An HTTPS-only host has nothing the app can
speak to over the link, so OPEN is refused and the feature shows as unavailable; adb reverse tunnels to the
HTTPS port anyway and is unaffected.

Unlike adb reverse, a native link is not evidence of a computer the user authorised. The dialler binds its
socket to `127.0.0.1:0` first and registers that endpoint with the device key in `BridgedConnections`, then
connects. Connection middleware on the public listeners takes the entry when Kestrel accepts the connection
and stamps an `IBridgedConnectionFeature` on it, so the stamp lives exactly as long as the Kestrel
connection. An entry nobody took outlives its socket by a short grace, because Kestrel may accept after the
dialler already closed.

A stamped connection is not a local request: `LoopbackConnection.IsLocalRequest` is false for it, so the
plugin endpoints stay closed to it, including to other apps on the phone. Loopback trust is unchanged.
Throttles and rate limits key a bridged request on its device (`usb:<serial>`, or `usb:<bus>-<port path>`
without a serial, and `usb:<udid>` on iOS) instead of 127.0.0.1, keeping the `|username` suffix, so one phone
cannot lock out another or the desktop. adb reverse traffic is never stamped and keeps ADR 0030's behaviour.

### Android: nothing is sent to a device nobody chose

The host polls libusb every three seconds (`AccessoryCoordinator`). Descriptors are read once per plug (bus
and port path) and never reopened by the poll; string descriptors are read only for devices whose
descriptors already look like an Android phone: not Apple, device class 00/EF/FF, and every interface ADB,
MTP/PTP or a vendor interface named MTP. Products matching the Car Thing markers are excluded.

A device is switched only after the admin chose it in Settings (Connect without debugging) or when its
serial was remembered because it linked before. No vendor request reaches any other device.

For a device the admin picked or that is remembered, USB without debugging takes priority over adb: it is
switched even while adb lists it and host ADB is enabled, and phones that only expose ADB are listed so they
can be picked. The pick is the admin's explicit consent to take that phone away from adb; the desktop says so
next to the action when adb knows the device, because other programs' adb sessions to it (scrcpy, Android
Studio) end with the switch. Devices nobody picked keep ADR 0030's behaviour and are never switched while adb
knows them. There is no duplicate path: while a device is switching or linked, the adb tunnel coordinator
asks `INativeUsbSerials` and sets up no reverse tunnel for its serial, and forgets what it had cached for it,
so adb reverse starts fresh if the device falls back to adb.

A switched phone that never links is not switched again automatically until it is unplugged; an explicit
pick by the admin is consent to try again and starts the plug's switch history over. A pick never overrides
a BYE: the phone user closed the link, so it stays closed until the phone is unplugged. After an intentional BYE the
accessory is neither reopened nor switched again until the phone is unplugged. A device that was absent for
even one poll is identified again by its serial before a pick, a remembered link or re-switch history
carries over to it, so another phone on the same port is a new, unpicked device. After a loss without BYE it is switched again at most
three times per plug, after 5 s, 30 s and 2 min. Unplugged means absent from that port path for two polls.
The policy is one record (`AccessoryReconnectPolicy`) so it can follow what hardware shows.

### iOS: the host connects to the app

For every USB-attached device usbmuxd reports, the host tries port 8197 every three seconds until it links,
one link per device. The app listens only while it is in front and says BYE before it goes to the
background, which the host treats as information: it keeps trying, and is refused until the app listens
again. No pairing is initiated by the host.

### Threat model

| Threat | Answer |
| --- | --- |
| A charger or foreign computer presents the accessory strings and plays host | The app accepts a native port only when the host signs a loopback endpoint and asks the user to compare the fingerprint before signing in to a computer it does not know. |
| A computer relays the link to the real host over the LAN | The relay's socket is not loopback on the host, so the signed endpoint is not loopback and the app refuses it. |
| Another iPhone app listens on 8197 or talks to the bridge's 8193 | The host only dials its own listener and never trusts a bridged connection as local; the app's fingerprint comparison covers a foreign listener. |
| A bridged client reaches loopback-only or plugin endpoints | Bridged connections land on the public listener (never admin) and are stamped as not local. |
| One device exhausts a shared throttle bucket | Throttles and rate limits are keyed per device. |

## Consequences

- The feature starts experimental: `usb.nativeEnabled` defaults to off, and while it is off nothing touches
  libusb or usbmuxd.
- libusb is not bundled. Android needs `libusb-1.0` from the system or a package manager
  (`libusb-1.0.dll`, `libusb-1.0.0.dylib` including `/opt/homebrew/lib` and `/usr/local/lib`,
  `libusb-1.0.so.0`); without it only the Android part is unavailable. Windows additionally needs the WinUSB
  (or libusbK) driver bound to the phone before and after switching. Linux needs a udev rule for USB access
  and, for iPhones, the usbmuxd daemon.
- While switched, the phone offers no file transfer (MTP) and ends its own adb sessions; without the app it
  shows a prompt that no app handles the accessory.
- The app speaks plain HTTP over the link, so an HTTPS-only host has no USB connection without debugging.
- Real hardware behaviour cannot run in CI. The protocol engine, the plug state machine (behind a libusb
  seam), usbmux encoding and the stamp are unit tested; the observations below are recorded by hand.

### Reconnect observation

Observed on a Xiaomi POCO C85 (HyperOS, Android 15) against a macOS host: when the app is force-stopped
while the phone is plugged in, the phone stays in accessory mode (18D1:2D01). The host saw the peer go silent
after about 13 s and showed the phone as waiting for the app; reopening the app linked again within about a
second through the HELLO epoch reset, without a re-switch. The re-switch branch of `AccessoryReconnectPolicy`
(5 s, 30 s, 2 min after a loss without BYE) was not exercised by this phone and stays as the fallback for
phones that leave accessory mode. Unplugging and replugging a remembered phone switched it again by itself,
and it linked about 3 s after the switch. After a host restart, with a changed public port, Android and iOS
links came back by themselves within seconds.

### Hardware verification

Host: macOS on Apple silicon with libusb 1.0.30 (arm64, Homebrew). Phones: Xiaomi POCO C85 (HyperOS,
Android 15) and iPhone 11 (iOS 27).

| Case | Descriptor | Result |
| --- | --- | --- |
| Android, USB debugging on | 2717:FF08, ADB only (ff/42/01) | Listed with host ADB off, and a pick switched it to 18D1:2D01 with the same serial. Observed before native priority, when ADB-only phones were hidden while host ADB was on |
| Android, debugging off, charging only | 2717:FF40, one PTP interface (06/01/01) | Listed; a pick switched it to 18D1:2D00, linked in about 6 s, the deck worked and the phone charged |
| iPhone | usbmuxd, port 8197 | Linked; fingerprint comparison before sign-in, deck rendered, reconnected after background and foreground, the phone charged |
| Both at once, 10 minutes | AOA and usbmuxd | No link loss or restart, no 429, no warnings or errors in the host log |
| Bridged request to `/api/plugins/protocol` | | 403 over the link, 200 locally |
| Android, USB debugging on, host ADB on, served by adb reverse (8194 to 8292) | 2717:FF08, ADB only (ff/42/01) | Native priority: listed as connected through ADB with the connect action offered; a pick switched it anyway. The row showed switching without a connect action, then waiting for the app, and it linked after about 9 s in 18D1:2D01. adb listed it as a device again and the host set up no reverse tunnel for it (no tunnel, no error); comparison, sign-in and the deck worked. Turning USB without debugging off brought the host's adb reverse back within 3 s while the phone was still in 18D1:2D01. No unobserved task exceptions in the host log |

Found on the way, and reflected in the guide:

- macOS on Apple silicon holds a freshly switched accessory back until the user allows "Allow accessory to
  connect?" once. Android left accessory mode in the meantime and came back in normal mode; since it had been
  absent, the host identified it again and listed it as a new, unpicked device. A second pick then switched
  it and it linked at once, without a replug, for 2D01 and 2D00. A phone that comes back within one poll keeps its plug state and shows as stopped; an explicit pick
  switches it again there as well, since only automatic re-switching waits for a replug.
- Android asks "Open Macro Deck for <description>?", behind the lock screen until the phone is unlocked. With
  "always" ticked, later plugs open the app without asking. The description is the computer's name.

Latency proxy: time to first byte of `GET /api/system/build-info` from the phone with toybox `nc`, 30 samples
each. End-to-end input latency was not instrumented.

| | AOA | adb reverse | Wi-Fi | Phone-local baseline |
| --- | --- | --- | --- | --- |
| Median | 80.2 ms | 69.0 ms | 117.8 ms | 79.7 ms |
| p90 | 93.3 ms | 74.5 ms | 222.9 ms | 92.0 ms |

The method resolves about 10 ms and is dominated by process start: AOA and adb reverse add less than it
resolves, Wi-Fi adds about 38 ms at the median plus a long tail (maximum 389.7 ms).

### Feasibility assessment

Android: AOA has been part of Android since 3.1, below the app's floor of Android 6, so every device the app
supports has the protocol unless its manufacturer disabled it. It works on the verified phone with debugging
on and off; other vendors are not verified yet and their exceptions are recorded here as they are found.

iOS App Store: the app only listens on a BSD socket on its own loopback, and the computer connects through
Apple's usbmuxd, as PeerTalk-based apps on the App Store do. No private API or entitlement is involved, and a
loopback socket does not trigger the local network permission. The approach is expected to comply with the
public API rule; it has not been submitted for review, so this is not confirmed. The link works only while
the app is in front, since iOS suspends background sockets.

Not verified: Windows and Linux hosts, Android phones from other vendors, other iPhones.

## References

- [Macro-Deck#925](https://github.com/Macro-Deck-App/Macro-Deck/issues/925), Macro-Deck-Companion#68
- [ADR 0030](0030-android-usb-connections-over-adb.md), [ADR 0003](0003-loopback-trust-token-scopes-and-device-identity.md), [ADR 0086](0086-host-identity-key.md)
- [Android Open Accessory protocol](https://source.android.com/docs/core/interaction/accessories/aoa)
