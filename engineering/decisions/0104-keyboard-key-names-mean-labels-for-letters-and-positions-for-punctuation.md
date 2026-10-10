# ADR 0104: Keyboard key names mean labels for letters and positions for punctuation

Status: Accepted

## Context

Keyboard combos are persisted as `{"modifiers": [...], "key": "<name>"}` in Press Key, Key Down, Key Up,
keyboard sequences and event triggers, and plugins publish the same shape. The names were interpreted
differently by each part: the recorder stored the typed character for punctuation (`-`, `_`, `SS`), the
Select list stored `KeyboardEvent.code` names, Windows pressed layout-defined `VK_OEM_*` codes, Linux
the key typing the character, macOS a US key position even for letters, and trigger matching compared
the raw strings.

A browser reports the label only for letters it can type (`event.key`) and the US position for every key
(`event.code`). Most punctuation characters have no unshifted key on other layouts (German `[` is
AltGr+8), so a character cannot name a key that every layout has.

## Decision

- One vocabulary, the host's `KeyCode` names, for recording, selecting, executing and matching.
- `A` to `Z` name the key labeled with that letter on the active layout (unchanged since the recorder
  rule for AltGr and Shift). Windows and macOS resolve the label against the current layout at press
  time; Linux goes through the X keysym table, which takes the first layout group that has the letter, so
  a multi-group setup (`us,de`) presses the first group's key.
- Digits, the eleven US punctuation names (`Minus`, `Equal`, `BracketLeft`, `BracketRight`, `Backslash`,
  `Semicolon`, `Quote`, `Comma`, `Period`, `Slash`, `Backquote`) and `IntlBackslash` name a physical
  position, spelled like `KeyboardEvent.code`. On macOS the position is the key code WebKit reports, so
  ISO Apple keyboards keep WebKit's naming of key codes 10 and 50.
- Every older spelling stays accepted through one alias table (`KeyNames` in `MacroDeck.Sdk.Input`),
  which trigger matching uses as well. Unknown names keep their literal, case-insensitive meaning.

## Consequences

- A recording replays the same key on the machine it was recorded on, whatever the layout.
- Punctuation stored before this change on a non-US layout changes key on Windows and Linux: a character
  the old recorder stored (`-` recorded with the German `-` key), a name picked from the Select list, or a
  Macro Deck 2 migration used to press the key that types that character and now presses the US-position
  key. Values recorded on a US layout keep their key. This was accepted to give every punctuation name one
  meaning across recording, executing and matching; such combos have to be recorded again. New Macro Deck
  2 migrations translate `VK_OEM_*` through the migrating machine's layout.
- Values stored by the old recorder that never resolved (`SS`, `Ü`, `_`) still do not and are logged.
- Keys whose US position is a letter but which type punctuation on the active layout (the AZERTY comma
  key) record as that letter; telling them apart needs a layout map the desktop app's WebView does not
  expose.

## References

- [Keyboard combos](../../docs/src/content/docs/features/events.md#keyboard-combos)
- Issues #1278, #1279, #1280
- [ADR 0105](0105-the-sdk-translates-native-key-codes-with-guarded-platform-interop.md): plugins reach the same vocabulary from native key codes
