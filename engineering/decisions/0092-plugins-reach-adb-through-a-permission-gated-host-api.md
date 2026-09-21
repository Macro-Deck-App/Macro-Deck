# ADR 0092: Plugins reach ADB through a permission-gated host API

Status: Accepted

## Context

[ADR 0030](0030-android-usb-connections-over-adb.md) put adb under host ownership: one shared server,
tracked child processes, and an `IAdbManager` that accepts a closed command union with no argv or
command-string case. It kept `IAdbManager` out of the SDK and left third-party plugin access to adb as a
separate decision with a wider blast radius.

[#929](https://github.com/Macro-Deck-App/Macro-Deck/issues/929) asks for that access. Plugins that work
with Android devices otherwise ship their own adb client, which fights Macro Deck's server for the same
USB devices, the exact problem ADR 0030 avoids by never running a private server. What such plugins need
is open-ended (arbitrary shell commands, file transfer, app installs), so the closed command union cannot
serve them.

Manifest permissions were declared and never enforced, because every existing plugin declares none and a
default-deny posture would break them all.

## Decision

**Plugins get a separate host API, `adb`, not `IAdbManager`.** The SDK exposes `IAndroidDeviceManager` and
`IAndroidDevice` in `MacroDeck.Sdk.Android`, resolved from dependency injection, not from
`IIntegrationContext`. On the wire it is the `adb` host API: eight operations (shell, battery, push, pull,
install, uninstall, package-installed, connect) and a per-plugin `host.state` push of access and devices. The host's
own command union stays closed; the plugin path is a parallel, validated entry into the same manager and
the same adb server.

**Plugins get arbitrary shell.** A shell command is passed to the device's shell as given. The host
validates only what protects adb itself: a non-empty command of at most 8 KiB that does not start with `-`,
absolute local and device paths, and Android's package-name grammar. Commands get no standard input.

**Access is a conjunction the user controls.** A plugin may use ADB only when ADB is enabled, the user
setting "Allow plugins to use ADB" is on (default on), and the installed plugin's active manifest declares
`host:adb`. A self-registered session, admitted through a developer token or interactive pairing, is
exempt from the declaration: the user admitted it by hand and it has no installed manifest to read.

**`host:adb` is the first enforced manifest permission.** It can be enforced from the start because no
existing plugin relies on it. Every other permission stays declared and unenforced.

**Consent is asked at install time.** Installing a version that declares `host:adb` while ADB or plugin
access is off raises a notification, which the desktop app also presents as a dialog, offering to turn
both on. An update of a version that already
declared it does not ask again. A plugin that declares it and is refused a call asks the same question
once per host run, so an answer skipped before a restart is asked again. The install dialog and the plugin's page disclose the permission.

**Local paths are host paths, used with the host's identity.** A push, pull or install names an absolute
path on the machine Macro Deck runs on, and adb reads or writes it as the Macro Deck user.

**Calls run off the session dispatch loop, capped per plugin.** An adb call can run for minutes, so it
leaves the per-session loop every other `host.invoke` shares. At most four run per plugin; a fifth is
refused at once with a retryable `RATE_LIMITED`, never queued. `host.cancel` ends a running call, which
then gets exactly one `CANCELLED` result.

**Plugins may connect devices over the network.** `connect` runs `adb connect host:port` on the host's
shared adb server, so a device a plugin connects is visible to every plugin and in the ADB settings, which
offer the same action to the user. Pairing (Android 11 and later) is not offered.

**A locked host changes nothing.** While Macro Deck is locked, shell, push, pull, install, uninstall and connect
are refused with `CAPABILITY_UNAVAILABLE` and reason `host_locked`. Battery and package queries stay
available.

**Output is capped.** Shell output is cut to fit one protocol message and flagged `Truncated`, rather than
failing or streaming.

**The SDK interfaces grow only by default interface members**, so a plugin compiled against this version
keeps loading when members are added.

## Consequences

- The permission is a user-control gate, not isolation. A plugin process is unsandboxed and can start its
  own adb; `host:adb` governs only Macro Deck's connection, and the security model says so.
- "Allow plugins to use ADB" is one switch for every plugin that declares `host:adb`. Allowing one plugin
  after the install notification allows them all. A per-plugin grant is left open for later.
- Access is evaluated per plugin, so the `adb` state push is per plugin rather than a broadcast.
- The protocol gains `ADB_NOT_ENABLED`, `ADB_NOT_ALLOWED` and `ADB_FAILED` plus `adb_` reasons. All are
  additive: an old plugin never calls `adb` and ignores the unknown push; a new plugin on an old host sees
  access `Unsupported` and gets `CAPABILITY_UNSUPPORTED` for every call.
- In-process integrations get no `IAndroidDeviceManager`; they keep using the host's own services.
- Real devices still cannot be exercised in CI (ADR 0030); validation, access, the in-flight cap, lock
  behaviour, cancellation and truncation are tested against fakes.

## Alternatives considered

- **Expose `IAdbManager` directly.** It is a host-internal contract shaped for the built-in integration's
  closed command set; publishing it would freeze it as SDK surface and still not offer arbitrary shell.
- **No permission, only the global switch.** Simpler, but gives the user no warning at install time and
  lets any plugin reach connected devices without saying so.
- **A per-plugin grant UI.** The most precise control, but it needs a consent surface and stored grants
  that no other permission has yet. The manifest declaration plus one switch covers the need now and does
  not preclude it.

## References

- [#929](https://github.com/Macro-Deck-App/Macro-Deck/issues/929)
- [Android devices](../../docs/src/content/docs/features/android-devices.md)
- [`PluginAdbAccessPolicy`](../../host/src/MacroDeckHost.Application/Plugins/PluginAdbAccessPolicy.cs)
