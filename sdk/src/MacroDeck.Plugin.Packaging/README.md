# MacroDeck.Plugin.Packaging

The plugin manifest and artifact format for [Macro Deck](https://github.com/Macro-Deck-App/Macro-Deck),
factored out of the host so the same code that validates a manifest and reads a `.macroDeckPlugin`
archive inside the host can also run in tooling that packs or validates one outside it.

What is in here:

- `MacroDeck.Plugin.Packaging.Manifest` - the `manifest.json` model (`PluginManifest` and the records it
  is built from), `IPluginManifestReader` and its implementation, and the runtime identifier and
  permission vocabularies a manifest is validated against.
- `MacroDeck.Plugin.Packaging.Artifacts` - the `.macroDeckPlugin` archive format: names and structural
  limits, the path-safety policy an archive entry is judged against, the signable artifact digest,
  `IPluginArtifactReader` and its implementation, and `PluginInstallError`.
- `MacroDeck.Plugin.Packaging.Versioning` - `SemanticVersion` and `SemanticVersionRange`, the small
  SemVer grammar compatibility and dependency declarations use.
- `PluginManifestJson.Options` - the single `JsonSerializerOptions` instance every manifest is read
  through, so a manifest can never be parsed by two divergently configured serializers.
- `AddMacroDeckPluginPackaging()` - registers `IPluginManifestReader` and `IPluginArtifactReader`.

This package has no dependency on the host; it depends only on
[`MacroDeck.Plugin.Protocol`](https://www.nuget.org/packages/MacroDeck.Plugin.Protocol) for the wire
types a manifest's declared compatibility range is checked against.

See [the plugin development documentation](https://docs.macro-deck.app/introduction/quickstart/).

Licensed under Apache-2.0.
