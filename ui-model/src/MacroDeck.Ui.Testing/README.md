# MacroDeck.Ui.Testing

The headless Macro Deck UI test host: render a view, query it, raise events at it and assert the patches it
produces, with no host process and no renderer involved.

```csharp
var host = UiTestHost.Render(view);

host.ClearPatches();
host.ById("apiKey").Change("k-1");
await host.SettleAsync();

var patch = host.LastPatch;
Assert.That(patch.Operations, Has.Count.EqualTo(1));
Assert.That(patch.Operations[0].Op, Is.EqualTo(UiPatchOperations.SetProperties));
Assert.That(host.ById("apiKey").Text(UiConfigProperties.Value), Is.EqualTo("k-1"));
```

Query with `ById`, `FindById`, `ByType`, `SingleByType`, `ByText` and `Root`; raise events with `Raise`,
`Change`, `Activate` and `Submit`; read properties with `Property`, `Text`, `Flag`, `Number` and
`HasProperty`. `SettleAsync` awaits asynchronous handlers and loads and throws rather than hanging when work
never completes, so a stuck loader fails a test instead of a build.

The property worth the dependency: **`Tree` is the applied tree.** After every drain the host folds each
patch onto its own copy of the previous tree and fails if a patch is rejected or if the result differs from
the view's own tree. Every query in every test therefore implicitly asserts that the producer only emits
patches it could apply itself - the desynchronisation a real renderer would otherwise be the first to find.

`Describe()` renders the tree as readable indented text and `DescribePatches()` does the same for patches,
for eyeballing a failure; `ToCanonicalJson()` gives the wire form. This package deliberately references no
assertion library, so it works with whichever one the test project already uses.

`UiTreeApplier` also lives here. It implements the tree-dependent patch rules `MacroDeck.Ui.Model` documents
but has no API for - a move's post-removal index, out-of-bounds rejecting rather than clamping, the
root-targeting rules, in-order application and atomic rollback. It moves into `MacroDeck.Ui` once a renderer
bridge needs it in production.

See [ADR 0038](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0038-ui-model-and-declarative-dsl.md)
for the design rationale.

References only `MacroDeck.Ui`.
