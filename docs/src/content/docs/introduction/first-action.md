---
title: Your first action
description: Add a minimal action to the template plugin and trigger it from a Macro Deck button.
---

If the [Quickstart](/introduction/quickstart/) ends with `Session established`, your generated
plugin is ready for its first behavior. The examples below use the Quickstart's
`Acme.LightControl` project; substitute the name and namespace you chose if they differ.

## Add the action

Create `src/Acme.LightControl/SayHelloAction.cs`:

```csharp
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace Acme.LightControl;

internal sealed class SayHelloAction : IActionDefinition
{
    public string Id => "say-hello";

    public LocalizedText Name => "Say hello";

    public LocalizedText Description => "Writes a greeting to the plugin console.";

    public IReadOnlyList<ActionParameter> Parameters { get; } = [];

    public IActionExecutor CreateExecutor() => new Executor();

    private sealed class Executor : IActionExecutor
    {
        public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
        {
            Console.WriteLine("Hello from Macro Deck!");
            return ActionResult.SucceededTask;
        }
    }
}
```

The action has no parameters. Its executor writes one line and reports success when Macro Deck invokes
it.

## Register the action

Open `src/Acme.LightControl/PluginIntegration.cs`. The template already exposes an empty
`Actions` collection; replace that line:

```diff
- public IReadOnlyList<IActionDefinition> Actions { get; } = [];
+ public IReadOnlyList<IActionDefinition> Actions { get; } = [new SayHelloAction()];
```

Restart the plugin after changing this collection so the host receives the updated action catalogue.

## Trigger it from Macro Deck

Follow [Debugging plugins](/guides/debugging/) to enroll the plugin once and start the
**Macro Deck - Real Host** profile from your IDE. Then:

1. Set a breakpoint on `Console.WriteLine("Hello from Macro Deck!");`.
2. Assign the plugin's **Say hello** action to a Macro Deck button.
3. Press the button.

The debugger stops in `ExecuteAsync`. Continue execution and the IDE console prints:

```text
Hello from Macro Deck!
```

If the action is not listed, confirm that the plugin reconnected after the code change and is ready;
the [debugging guide](/guides/debugging/) shows how to inspect readiness and connection diagnostics.

For parameters, failures and asynchronous work, continue with the
[Actions reference](/sdk/capabilities/#actions) or a
[worked sample plugin](/introduction/samples-and-template/).
