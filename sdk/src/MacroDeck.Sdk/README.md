# MacroDeck.Sdk

Capability contracts for [Macro Deck](https://github.com/Macro-Deck-App/Macro-Deck) integrations and
plugins. This package holds the interfaces and records you implement, plus the small `MacroDeck.Sdk.Input`
helpers that read the operating system's active keyboard layout. It has no dependency on the host.

What is in here:

- `IIntegration` and `IIntegrationContext` - the base contract and the host services handed to it.
- Capability interfaces - actions, config flow, variables, events, music players, weather stations,
  calendars, virtual profiles, icons and issues.
- `MacroDeck.Sdk.Identity` - `MacroDeckId` and `QualifiedId`, the id rules every owner and capability
  is validated against.
- `MacroDeck.Sdk.Input` - `KeyCode`, `KeyNames` and `NativeKeys`, which translate the key codes of a
  native keyboard hook into the names a `KeyboardCombo` stores.

To build an out-of-process plugin, reference [`MacroDeck.Plugin.Hosting`](https://www.nuget.org/packages/MacroDeck.Plugin.Hosting)
as well; it brings this package with it.

See [the plugin development documentation](https://docs.macro-deck.app/introduction/quickstart/).

Licensed under Apache-2.0.
