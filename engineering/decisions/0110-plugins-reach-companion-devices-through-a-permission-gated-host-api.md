# ADR 0110: Plugins reach Companion devices through a permission-gated host API

Status: Accepted

## Context

The phones and tablets running the Companion app report their state to the host (battery, orientation,
brightness, model, network, focus, CPU and memory) and accept commands (brightness, orientation, vibration,
screen on and off, bringing Macro Deck to the front) and screenshots of the deck or the whole screen. A full
screen screenshot and some commands need a permission the device's user grants on the device.

The built-in Companion integration reached all of this through a host-only gateway, `ICompanionGateway`,
which the host injected through a consumer interface. [ADR 0107](0107-built-in-integrations-are-built-as-extractable-plugins.md)
requires built-in integrations to reach the host only through the SDK, so the gateway is debt, and an
integration built on it could not move out of process. Plugins had no way to reach Companion devices at all.

Device state includes the Wi-Fi network's name, commands act on a device in someone's hand or on a wall, and a
screenshot can show whatever the deck shows. That is closer to operating a device, which
[ADR 0092](0092-plugins-reach-adb-through-a-permission-gated-host-api.md) gates on `host:adb`, than to a
battery level, which [ADR 0109](0109-battery-devices-are-a-host-brokered-catalog-every-plugin-may-read.md)
lets every plugin read.

## Decision

**Companion devices are a public SDK capability.** `ICompanionDeviceApi` in `MacroDeck.Sdk.Companion`, reached
through `IIntegrationContext.CompanionDevices` and, in a plugin, from dependency injection. It offers access,
the connected devices with their last report, `DeviceReady` and `DeviceChanged`, `WaitForDeviceAsync`,
`SendAsync` (fire and forget), `RequestAsync` (waits for the device's answer: done, unconfirmed or a failure)
and `CaptureScreenshotAsync`. Failures are values, not exceptions. `IIntegrationContext.CompanionDevices` is a
default interface member returning `UnsupportedCompanionDeviceApi.Instance`, and later members arrive as
default interface members.

**A device's connection is identified by `ReadySequence`.** It changes exactly when a device becomes ready
again after it was gone, so a reader can act once per connection however often it sees the device.

**`host:companion-devices` is the second enforced permission.** A plugin may use Companion devices only when
the user setting **Allow plugins to use Companion devices** is on and the active manifest of the installed
plugin declares `host:companion-devices`. Unlike ADB's setting, it is off by default, so no plugin reaches a
device until the user has decided; nothing depended on plugin access before. A
self-registered session counts as declaring it, for the reason given in ADR 0092; the setting still applies
to it. The permission is enforced from the start because no existing plugin relies on it.

**Consent follows the ADB pattern.** Installing a version that newly declares the permission while plugins
are not allowed raises a notification, shown by the desktop app as a dialog whose **Allow** turns the
setting on. A plugin that declares it and is refused a call asks once per host run; one that does not declare
it is refused without asking. The install dialog and the plugin's page disclose the permission. The setting
is one switch for every plugin that declares it.

**The device's own consent is untouched.** Allowing plugins in Macro Deck never grants a capability the
device's user has not granted on the device. A full screen screenshot still prompts on the device and fails
with `ConsentDenied` when declined.

**Built-in integrations are exempt.** For them `Access` is always `Available`. The built-in Companion
integration uses only `IIntegrationContext.CompanionDevices`, and the host-only gateway is removed. Extracted
into a plugin, it would need the permission and the user's consent like any other.

**On the wire it is the `companion-devices` host API.** Operations `wait`, `send`, `request` and
`screenshot`, run off the session's dispatch loop with at most four in flight per plugin and `host.cancel`
honoured, plus a revisioned, per-plugin `host.state` push of access (`available` or `not-allowed`) and the
devices. A refused call is `COMPANION_DEVICES_NOT_ALLOWED`. A screenshot does not fit one protocol message,
so its PNG follows over `host.asset.*` with the new host-to-plugin asset kind `companion-screenshot`, as the
`adb` screenshot does.

## Consequences

- An allowed plugin learns everything a device reports, the Wi-Fi network's name included, and can capture
  the deck, other plugins' widgets included, without a prompt on the device. The security model says so.
- Because the setting is off by default, a plugin built around Companion devices does nothing until the user
  answers the consent dialog; its own UI has to say what to turn on.
- Access is evaluated per plugin, so the push is per plugin rather than a broadcast, and a plugin that is not
  allowed is told once rather than on every device report.
- The protocol gains a host API, an error code and an asset kind, all additive in major `3`. An old plugin
  ignores the push; a new plugin on an old host sees access `Unsupported`, and its operations answer as not
  connected without throwing.
- The host still creates the Companion integration's configuration entry for a newly connected device, which
  names that integration from host code; that line stays in the integration boundary baseline as debt.
  Before the integration's first initialization, its configuration entries report as disconnected.
  Update 2026-10-13: the debt is paid. The integration declares the `companion-device-entries` capability
  kind and the host creates entries for the first declaring integration, a plugin only once every built-in
  has registered; the baseline line is gone.
- A per-plugin grant is still left open, as in ADR 0092.

## Alternatives considered

- **No permission, readable by every plugin like batteries.** Rejected: device state includes the network's
  name, and commands and screenshots act on the device. ADR 0109's reasoning, that the data is what a deck
  shows anyway, does not hold for them.
- **Reuse `host:devices`.** Rejected: it names the `devices` host API for device providers, which every
  existing plugin may call without declaring it. Enforcing it would break those plugins, and leaving it
  unenforced would not gate Companion devices.
- **Keep a host-only gateway for the built-in integration.** Rejected by ADR 0107: the integration could not
  be extracted, and plugins would still have no access.

## References

- [#1297](https://github.com/Macro-Deck-App/Macro-Deck/issues/1297)
- [Companion devices](../../docs/src/content/docs/features/companion-devices.md)
- [`PluginCompanionDeviceAccessPolicy`](../../host/src/MacroDeckHost.Application/Plugins/PluginCompanionDeviceAccessPolicy.cs)
