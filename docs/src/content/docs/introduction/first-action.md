---
title: Your first action
description: Add an action with a parameter to the template plugin, trigger it, and show its state on the button.
---

This continues the [Quickstart](/introduction/quickstart/) project, `Acme.LightControl`. You add a
**Set brightness** action with a 0-100 slider, trigger it, and then make the button show whether the
light is on.

## 1. Add the strings

Every user-facing string lives in `src/Acme.LightControl/Localization/Strings.resx`. Add these entries
before `</root>`:

```xml
<data name="Actions.SetBrightness.Name" xml:space="preserve">
  <value>Set brightness</value>
</data>
<data name="Actions.SetBrightness.Description" xml:space="preserve">
  <value>Sets the light to a brightness between 0 and 100.</value>
</data>
<data name="Actions.SetBrightness.Brightness.Label" xml:space="preserve">
  <value>Brightness</value>
</data>
```

The build turns each key into a method on the generated `Strings` class, for example
`Strings.Actions.SetBrightness.Name()`. See [Localization](/features/localization/).

## 2. Add the action

Create `src/Acme.LightControl/SetBrightnessAction.cs`:

```csharp
using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using Serilog;

namespace Acme.LightControl;

public sealed class SetBrightnessAction(ILogger logger) : IActionDefinition
{
	private double _brightness;

	public string Id => "set-brightness";

	public LocalizedText Name => Strings.Actions.SetBrightness.Name();

	public LocalizedText Description => Strings.Actions.SetBrightness.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Slider("brightness", 0, 100,
			label: Strings.Actions.SetBrightness.Brightness.Label(),
			defaultValue: 100),
	];

	public IActionExecutor CreateExecutor() => new Executor(this, logger);

	private sealed class Executor(SetBrightnessAction action, ILogger logger) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var brightness = Convert.ToDouble(
				context.Parameters.GetValueOrDefault("brightness")?.ToString() ?? "100",
				CultureInfo.InvariantCulture);

			action._brightness = brightness;
			logger.Information("Brightness set to {Brightness}", brightness);
			return ActionResult.SucceededTask;
		}
	}
}
```

- `Id` is stored in users' profiles. Never rename it once released.
- The executor reads the parameter by name from `context.Parameters`.

## 3. Register it

In `src/Acme.LightControl/PluginIntegration.cs`:

```diff
- Actions = [new LogMessageAction(logger)];
+ Actions = [new LogMessageAction(logger), new SetBrightnessAction(logger)];
```

```bash
dotnet build
```

## 4. Trigger it from a test

Add to `tests/Acme.LightControl.Tests/PluginIntegrationTests.cs`, inside `PluginIntegrationTests`:

```csharp
[Test]
public async Task Set_brightness_logs_the_new_value()
{
	await using var harness = CreateHarness();
	await harness.InitializeIntegrationsAsync();

	var outcome = await harness.Actions.ExecuteAsync(
		"set-brightness",
		new Dictionary<string, object?> { ["brightness"] = 40 });

	Assert.That(outcome.Succeeded, Is.True);
	Assert.That(harness.Logs.Events.Any(e => e.Message.Contains("Brightness set to 40")), Is.True);
}
```

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:     8, Skipped:     0, Total:     8
```

The harness runs your action through the same capability handler the host calls. See
[Testing plugins](/features/testing/).

## 5. Trigger it from a button

Run the plugin against Macro Deck - press F5 in your IDE or run it with the CLI, see
[Debugging plugins](/guides/debugging/):

```bash
macrodeck-plugin run --project src/Acme.LightControl
```

Put **Set brightness** on a button, pick a value, lock the deck and press the button. The plugin output
shows:

```text
[plugin]       Brightness set to 40
```

Restart the plugin after changing its `Actions` so the host receives the new list.

## 6. Optional: show the state on the button

Implement `IStateProviderActionDefinition` so a button can follow the light:

```diff
- public sealed class SetBrightnessAction(ILogger logger) : IActionDefinition
+ public sealed class SetBrightnessAction(ILogger logger) : IActionDefinition, IStateProviderActionDefinition
```

```csharp
public Task<ActionStateSnapshot?> GetActionStateAsync(
	IReadOnlyDictionary<string, object?> parameters,
	CancellationToken cancellationToken)
{
	ActionStateDefinition[] states =
	[
		new("off", MacroDeckStrings.States.Off()),
		new("on", MacroDeckStrings.States.On()),
	];
	return Task.FromResult<ActionStateSnapshot?>(new(states, _brightness > 0 ? "on" : "off"));
}
```

Check it in the test:

```csharp
var state = await harness.Actions.GetActionStateAsync("set-brightness");
Assert.That(state.Data!.Value.GetProperty("activeStateId").GetString(), Is.EqualTo("on"));
```

And against the conformance suite, which now checks the state snapshots too:

```bash
macrodeck-plugin test --project src/Acme.LightControl
```

```text
Passed: 27, Failed: 0, Skipped: 22
Conformant: yes
...
[PASS] MDC0309 Every state-provider action's state operation returns a well-formed snapshot (Required)
[PASS] MDC0310 Every state a state-provider action returns has an id that is a valid declared-kind identifier (Required)
```

In Macro Deck, a button running **Set brightness** can now show "On" or "Off". See
[Button states](/features/button-states/).

## Next steps

- [Actions](/features/actions/) - parameter types, failures, long-running work.
- [Button states](/features/button-states/) - default appearances, polling, expected states.
- [Features](/features/) - everything else a plugin can offer.
