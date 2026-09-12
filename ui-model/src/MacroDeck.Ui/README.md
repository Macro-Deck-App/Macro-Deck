# MacroDeck.Ui

The declarative half of Macro Deck UI: a plugin defines a view in C#, and a fine-grained reactive runtime
turns that view into a tree and every subsequent change into a patch.

This package ships the DSL, the runtime and two primitive vocabularies: the configuration profile and
the widget profile. It defines no host routing and no renderer.

```csharp
private readonly UiState<string> _apiKey = new(string.Empty);

private UiFlow Build() => new()
{
    Key = "setup",
    Title = "Connect your account",
    Children =
    [
        new UiStep
        {
            Key = "credentials",
            Events = [UiEventHandler.On(UiConfigEvents.Submit, Submit)],
            Children =
            [
                new UiStringInput
                {
                    Key = "apiKey",
                    Label = "API key",
                    Binding = Bind.To(_apiKey),
                },
            ],
        },
    ],
};
```

Two contracts are worth knowing before writing a view.

**Every key is yours to supply, and an id is never derived from a position.** A structural node's id is the
dot-joined path of keys from the root, so the step above is `setup.credentials`. An input's id is its bare
key, because an input-bearing node's id *is* the field name it submits as - only an `object` or `array`
container composes a dotted path for the inputs inside it. `UiWhen` and `UiFragment` are transparent: they
emit no node and contribute no path segment, so wrapping an element in a condition never renames it. A
`UiRepeat` contributes each item's key from its key selector, and there is deliberately no overload that
hands you an index.

**A bound value is a cell, not a snapshot.** Each property is evaluated inside a tracking scope, so a state
change re-evaluates only the cells that read that state and emits one `set-properties` operation per node
carrying only the keys that actually changed. The size of a patch is a function of what changed, never of the
tree's depth or size, and untouched subtrees are shared by reference rather than rebuilt.

The 27 input primitives and their type strings are derived from what the existing configuration system
already renders, one per `ActionParameterType`, so a renderer's mapping is the identity function. The 14
chrome primitives each correspond to something a configuration flow renders today. Both vocabularies are
open: an unrecognised type degrades through a node's fallback rather than failing the tree.

See [ADR 0038](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0038-ui-model-and-declarative-dsl.md)
for the design rationale and
[the authoring guide](https://docs.macro-deck.app/ui/)
for the walkthrough. Test a view with no host and no renderer using `MacroDeck.Ui.Testing`.

References only `MacroDeck.Ui.Model` and `MacroDeck.Localization`, zero `PackageReference`.
