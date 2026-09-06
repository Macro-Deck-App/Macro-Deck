# MacroDeck.Ui.Model

The transport-neutral core of Macro Deck UI: a declarative, renderer-independent UI framework so a
plugin defines UI in C# and each client (Angular now, Compose later) renders a neutral tree.

This package ships the model only - node identity, properties and patches, capability negotiation,
resource handles, the surface concept and canonical serialization. It defines no node types and
validates no surface kinds. A node is `{id, type, properties, children, fallback}` where `type` is an
arbitrary string and `properties` is an arbitrary JSON map. Configuration primitives (`text-field`,
`step`) and widget primitives (`image`, `chart`, `progress`) are both *profiles* - vocabularies living
in those two open slots - and neither ever appears in this package.

No DSL, no diffing implementation, no host routing, no renderer, no widget primitives. See
[ADR 0038](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0038-ui-model-and-declarative-dsl.md)
for the design rationale.

Zero `ProjectReference`, zero `PackageReference`.
