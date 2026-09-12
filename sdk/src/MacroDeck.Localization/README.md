# MacroDeck.Localization

Renderer-independent localization for Macro Deck integrations and plugins.

A localized string is referenced, not resolved, at the point it is written. `LocalizedString` carries a
scope, a key and its formatting arguments, so the same Macro Deck UI session can be rendered in
different languages by different clients and the host stays the authority for the selected culture.

```csharp
new Text(MacroDeckStrings.Common.Save())
new Text(MyStrings.ConnectedAs(userName))
```

Plugins add `Localization/Strings.resx` (plus `Strings.de.resx` and friends) and reference
`MacroDeck.Plugin.Analyzers`, whose source generator turns those files into a strongly typed API and
reports `MDLOC001`-`MDLOC006` at build time.

See <https://docs.macro-deck.app/features/localization/>.
