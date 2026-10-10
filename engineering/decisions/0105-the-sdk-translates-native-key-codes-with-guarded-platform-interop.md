# ADR 0105: The SDK translates native key codes with guarded platform interop

Status: Accepted

## Context

A plugin that listens for global hotkeys receives native key codes (Windows virtual-key and scan codes, macOS
`kVK_*` codes, Linux evdev codes) and has to publish the exact key names the combo editor stores
([ADR 0104](0104-keyboard-key-names-mean-labels-for-letters-and-positions-for-punctuation.md)). The vocabulary
and the native tables lived inside the host, which a plugin cannot reference, so each plugin kept a hand-copied
table that drifted from the editor. Translating a macOS letter or a Windows punctuation key correctly needs the
active keyboard layout, which is an operating system call.

## Decision

- `KeyCode`, `KeyModifier` and `KeyNames` live in `MacroDeck.Sdk.Input`; the host consumes them from there, so the
  host and every plugin share one definition of the same key.
- `NativeKeys` translates native codes to `KeyCode`. It is the first part of the SDK package that calls the
  operating system: Carbon on macOS and user32 on Windows, only to read the active layout.
- The interop is internal, guarded by `OperatingSystem.IsMacOS()` and `OperatingSystem.IsWindows()`, never throws
  and falls back to US positions when the layout cannot be read. The public methods are thread-safe.
- Linux translates evdev codes by US position without a layout lookup. Reading the active layout there needs X11
  or XKB, which the SDK does not take on.

## Consequences

- The SDK package is no longer pure contracts: it carries native imports that only run on the two platforms and
  have no effect elsewhere. New interop in the SDK needs a decision of its own.
- The SDK keeps its own reverse tables next to the host's forward tables. Host tests assert that both agree on
  every platform, so a table cannot change on one side alone.
- A hook on Linux names letters by US position until an XKB-based translation exists.

## References

- Issue #1281
