# ADR 0030: Android USB connections terminate on the public listener

Status: Accepted

## Context

The Android app can only reach the host over the network.
[#112](https://github.com/Macro-Deck-App/Macro-Deck/issues/112) asks for USB as an additional transport
where Wi-Fi is unavailable, blocked or unstable. The app is the client and the host is the server, so the
phone needs something to dial, which makes `adb reverse` — a port on the device that reaches the host —
the applicable direction.

Macro Deck 2 had recurring trouble with leftover adb processes holding resources during updates, so
process ownership is part of the problem rather than an implementation detail.

## Decision

### The tunnel terminates on the public listener, never the loopback listener

This is a security decision, not a convenience one. A connection arriving through an adb reverse tunnel
reaches the host *from the local adb server*, so its remote address is `127.0.0.1` and it satisfies every
clause of `LoopbackConnection.IsTrusted` except the port one
([ADR 0003](0003-loopback-trust-token-scopes-and-device-identity.md)). Pointing the tunnel at the
loopback listener would hand an unauthenticated phone full admin with no login. A test asserts the public
port is used on every code path, including the candidate fallbacks, and that the loopback port never
appears in any adb argument vector.

Because the tunnel lands on the public listener, a USB client is an ordinary remote client: the same
pipeline, the same login, the same scopes, the same ticketed WebSocket. There is deliberately no parallel
authentication path.

### The device-side port is a fixed contract, independent of the configured public port

The app probes a short fixed candidate list on `127.0.0.1` and identifies the host by a successful
response from the anonymous build-info endpoint rather than by the first open port. The host side follows
the live configured public port, but the device side cannot: a phone on USB has no discovery channel, so
it cannot learn that the user changed the public port. Tying the two together would leave the app dialling
a port it has no way to know. Probing by response rather than by open port matters because a mapping can
survive that no longer reaches this host. Reverse specs live in each device's own transport namespace, so
the same device-side port on two phones is not a conflict.

### Macro Deck never stops the shared adb server automatically

It starts the server if it is not running and records that it did, but never kills it on shutdown, on
disable, or during an update. The only place that command is issued is an explicit "Restart ADB server"
action whose confirmation says it affects other programs.

The adb daemon is a machine-global singleton that Android Studio, scrcpy or a terminal may adopt after
Macro Deck starts it, and killing it would break those tools. Running a private server on another port is
worse: adb servers claim USB devices exclusively, so a second server would fight the user's existing one
and neither would see the device reliably.

The Macro Deck 2 failure mode does not apply, because platform tools are not bundled: the adb binary
lives in the user's Android SDK or on `PATH`, never inside the install directory, so a running daemon
holds no Macro Deck file. **If platform tools are ever bundled, this decision has to be revisited**,
which is why server ownership is recorded at all. The optional platform-tools downloader is a narrower
version of that trigger, not the trigger itself: it is an explicit user action that can put `adb` inside
the *data* directory, which nothing here kills or replaces automatically.

What Macro Deck does own, and does clean up, is the child processes it spawned and the tunnels it
created. Shutdown removes its recorded tunnels, drains its own children, force-kills the remainder and
deletes its ownership marker, under a tight cap that is additive on top of every integration's shutdown.
**Termination is by tracked process id only**: Macro Deck never scans the process table and never matches
on image name, so it cannot terminate an adb instance it did not start.

### A device is contacted only when there is something to settle

A reverse mapping lives in the device's own adb transport and dies with it, so while a device stays
connected there is nothing to re-check. The reconcile pass establishes a tunnel once per device session
and then leaves that device alone until its serial leaves the device list or the public port moves; a
failing device backs off over successive passes.

This is a robustness rule, not an optimisation. Listing mappings on every pass meant contacting every
attached device several times a minute indefinitely, which a minimal `adbd` does not necessarily survive:
a jailbroken Spotify Car Thing answered a list with a protocol fault, dropped off the bus, rebooted, and
was contacted again three seconds later — a loop it never escaped until USB connections were switched off
entirely ([#727](https://github.com/Macro-Deck-App/Macro-Deck/issues/727)).

### Tunnel ownership is recorded per device serial

A state file records `(serial, devicePort, hostPort)` for every tunnel this installation created, and
stale mappings are matched by that identity rather than by a heuristic. A heuristic cannot work: if the
user changes the public port between runs, last run's mapping is indistinguishable from one another tool
created — and leaving it in place makes `--no-rebind` fall through to the next candidate while the app
still probes the first and lands on a dead tunnel, which presents as "settings say connected, phone
cannot connect". The marker's presence at startup means the previous run did not shut down cleanly, which
scopes the stale sweep.

### The manager exposes no arbitrary shell execution

`IAdbManager` accepts a closed command union with no argv or command-string case, and the integration
reaches it through a narrower port still. **Two layers of quoting are required and both are
implemented**: argument-list construction protects the host shell, and does nothing for the device, since
`adb shell` joins its arguments and hands the result to a shell running on the phone — so every
user-supplied value is additionally POSIX-quoted before it becomes part of a device-side command string.

## Consequences

- A USB-attached device passes `IsLocalRequest`, which has no port clause by design, so the plugin
  endpoints become reachable from it. They stay credential-gated and their throttles are keyed on
  credentials rather than on the source address, so nothing is granted. This is accepted, and it is the
  second reason not to relax the port clause.
- A USB device shares the login throttle bucket with local browser clients, because both present as
  `127.0.0.1`, so one phone retrying a wrong password can lock out another. Keying the throttle on the
  client-declared device id was rejected: that value is unauthenticated, so an attacker could rotate it
  and evade the throttle entirely, which is a worse trade than the availability annoyance.
- ADB infrastructure and the built-in ADB integration are separate enable states: the USB connection works
  with the integration disabled, and the integration owns no adb process or device list of its own.
- `IAdbManager` is deliberately **not** part of the SDK. If third-party plugins ever need controlled adb
  access, that is a separate decision with a wider blast radius.
- Real adb behaviour cannot be exercised in CI: USB permissions on Linux need udev rules, Windows needs
  OEM drivers, and the on-device authorization dialog requires a physical confirmation. Executable
  discovery and command construction are pure and tested for all three platforms; the rest is documented
  as a platform limitation. End-to-end verification also needs an Android release, since the app lives
  outside this repository.

## Alternatives considered

- **`adb forward` instead of `adb reverse`.** Opens a port on the host that reaches the device, which is
  the wrong direction.
- **A dedicated USB listener.** A third listener would have been implicitly trusted as admin by the trust
  predicate as it stood, and fixing that for a case that does not need it is the wrong trade.
- **A private adb server on a non-default port.** Would make kill-on-exit safe, but adb servers claim USB
  devices exclusively, so it would fight any server the user already runs.
- **Tying the device-side port to the configured public port.** Symmetrical, but an app on USB cannot
  discover a changed port.
