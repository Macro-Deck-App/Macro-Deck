# ADR 0109: Battery devices are a host-brokered catalog every plugin may read

Status: Accepted

## Context

Users want to see the batteries of their devices on a deck: the phone, the headset, the controller. The
knowledge is spread out. Macro Deck itself knows Android phones over ADB; plugins know headsets,
controllers and vendor peripherals. A Battery widget, the battery variables and any plugin that wants to
draw battery levels need one place to read all of them from.

Plugins are separate processes that reach the host only over the plugin protocol
([ADR 0026](0026-plugin-protocol-and-sdk-boundary.md)), and whatever crosses it is frozen once shipped.
ADB access for plugins is gated by `host:adb` and the user's **Allow plugins to use ADB**
([ADR 0092](0092-plugins-reach-adb-through-a-permission-gated-host-api.md)), and the `adb` state push is
filtered per plugin accordingly. A hardware serial is an identifier that should not leave the computer
or be handed to every plugin.

## Decision

**The host owns one catalog of battery devices.** Providers, built-in integrations and plugins alike,
implement `IBatteryProvider` and publish through `IBatteryProviderContext`; readers use
`IIntegrationContext.Batteries` (`IBatteryApi`). On the wire this is the `battery-provider` capability kind,
whose only operation is `describe`, and the `batteries` host API with `publish`, `remove` and `sync`.

**Providers push; the host never polls.** A provider decides how to watch its devices and publishes
changes. A plugin's SDK keeps the last published set and resends it in one `sync` after a resumed
reconnect; a new session initializes the provider again.

**Every plugin may read every device, without a permission.** The host pushes the whole catalog as a
revisioned `host.state` to every connected plugin, including the Android devices of Macro Deck's own ADB
provider, unfiltered by `host:adb` or **Allow plugins to use ADB**. This differs from ADR 0092 on purpose
and was the user's explicit decision: a name, a kind and a charge level are what a deck shows anyway, ADB
consent is about operating a device, and filtering would leave one plugin's battery widget blank for
another plugin's devices. The trust model states it.

**Ids are provider-local and qualified by the owner.** A provider chooses a stable local id; the host
stores the device under the authenticated plugin or integration, never under anything the payload claims,
and readers see `<owner>:<local id>`. Widgets and variables key on the qualified id.

**The ADB provider never publishes a serial.** Its local id is an HMAC-SHA256 of the device's hardware
serial (falling back to the adb serial) under a random key kept in this install's preferences, truncated.
The serial stays inside the ADB manager; it is not projected into the `adb` state, logs or the catalog.
The id is stable across USB and Wi-Fi and across restarts of the same install, a restored backup of the same
install keeps it, and a widget imported into another install shows its stored name as not available.

**The ADB provider is built on the public SDK alone** ([ADR 0107](0107-built-in-integrations-are-built-as-extractable-plugins.md)).
It reads devices, events and batteries from `IIntegrationContext.AndroidDevices`, takes the id from
`AndroidDeviceInfo.StableId`, skips a device until the host reports one, and tells a tablet from a phone with
`getprop ro.build.characteristics`. In process its access needs only ADB to be enabled; extracted into a
plugin it would need `host:adb` and **Allow plugins to use ADB**, as clarified in ADR 0092, and without them
its battery devices would disappear. The same `StableId` travels in the `adb` state push, so a plugin with
`host:adb` can match the devices it operates to their battery entries; it adds nothing a battery reader does
not already see.

**Removed and withdrawn are different.** `RemoveAsync` and devices left out of a `sync` are removed for
good: readers receive their ids in `RemovedIds`, and their variables are deleted. A provider that stops, an
integration or plugin the user turns off, a plugin session that ends and a host shutdown withdraw devices:
they leave the list without being reported as removed, variables keep their names and read as not connected, and the
devices return under the same id. A device that is only out of reach stays published as not connected. A
plugin turned off keeps publishing into a hidden store that appears again when it is turned back on.

**The host writes the battery variables under the provider's own integration.** Each device's variables
belong to the plugin or integration that publishes it, so a phone's appear under ADB and a headset's under
its plugin, and they go when that owner's variables are removed. They are pushed rather than polled, so an
unchanged level never makes them unavailable. There is no separate Batteries integration, and a provider
writes no variable code of its own.

**Bounded and atomic.** At most 64 devices per provider; ids and names at most 128 characters, a kind at
most 32. A publish or sync is all or nothing: one invalid device refuses the call with `INVALID_PAYLOAD`.
Kinds, charge states and health values are open strings that a reader maps to `other` or unknown when it
does not know them.

## Consequences

- Any installed plugin learns the names, kinds and levels of every battery device, including the user's
  phones. Providers must keep identifiers out of names and ids. Revisiting this would need a new,
  additive read gate; withdrawing data plugins already rely on is a compatibility break.
- Battery widgets, variables and plugins share one source, and new providers appear everywhere without
  host changes.
- A plugin that re-initializes its providers within one session keeps its earlier devices until it removes
  them; providers are told to remove what is gone for good.
- `RemovedIds` is best effort for a reader that was disconnected; the device list itself is always
  complete.
- The ADB id cannot be correlated across installs, so moving a configuration to another computer needs the
  user to pick the device again.
- New device kinds can be added without a protocol change; new icons for them follow the versioned
  `ui.icon` vocabulary of [ADR 0084](0084-built-in-icon-names-are-a-versioned-public-vocabulary.md).

## References

- [Batteries](../../docs/src/content/docs/features/batteries.md)
- [`MacroDeck.Sdk.Batteries`](../../sdk/src/MacroDeck.Sdk/Batteries/)
- [ADR 0107](0107-built-in-integrations-are-built-as-extractable-plugins.md)
- [`AdbBatteryProvider.cs`](../../host/src/MacroDeckHost.Integrations/Adb/Providers/AdbBatteryProvider.cs)
