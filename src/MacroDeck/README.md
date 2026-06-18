# SuchByte.MacroDeck

API assembly for developing [Macro Deck](https://github.com/Macro-Deck-App/Macro-Deck) plugins.

This is a **compile-time reference package**. Plugins compile against the API, but the
assembly is *not* copied to the plugin output — the Macro Deck host already has
`Macro Deck 2.dll` loaded at runtime and provides everything the plugin needs.

## Usage

Add the package to your plugin project:

```
dotnet add package SuchByte.MacroDeck
```

The reference is shipped under `ref/` only, so no runtime assembly is emitted to your
build output.

## Links

- [Repository](https://github.com/Macro-Deck-App/Macro-Deck)
- [License: Apache-2.0](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/LICENSE)
