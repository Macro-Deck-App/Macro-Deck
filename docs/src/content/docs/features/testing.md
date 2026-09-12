---
title: Testing plugins
description: Test actions, variables, events, configuration and devices in process with PluginTestHarness and the MacroDeck.Plugin.Testing fakes, then run the conformance suite.
---

`MacroDeck.Plugin.Testing` runs your plugin's own code against a fake host, with no Macro Deck
installation and no socket. The project template's test project already references it and uses NUnit;
the package works with any test framework.

## Quick start

```csharp
using MacroDeck.Plugin.Testing;
using NUnit.Framework;

public sealed class PluginIntegrationTests
{
	private static PluginTestHarness CreateHarness() =>
		PluginTestHarness.Create(builder => builder
			.UseLocalization(Strings.LocalizationCatalog)
			.RegisterIntegration<PluginIntegration>());

	[Test]
	public async Task The_example_action_writes_the_message_to_the_log()
	{
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();

		var outcome = await harness.Actions.ExecuteAsync(
			"log-message",
			new Dictionary<string, object?> { ["message"] = "Hello from a test" });

		Assert.That(outcome.Succeeded, Is.True);
		Assert.That(harness.Logs.Events.Any(e => e.Message.Contains("Hello from a test")), Is.True);
	}
}
```

`dotnet test` builds the plugin, runs the `log-message` action the way the host would and checks both
the result and the log line.

- **`Create` builds but does not start.** Call `InitializeIntegrationsAsync()` before invoking anything;
  an exception from an integration's `InitializeAsync` propagates to the test.
- **`harness.Context` is the fake host.** It replaces `IIntegrationContext`, and `harness.Clock` replaces
  `TimeProvider`. Neither substitution can be turned off.
- **Identity is generated.** Without a `PluginTestManifest` the harness writes one with a fresh, unique
  reverse-domain id, the name `Test Plugin` and version `1.0.0`. Pass your own only when the id, name,
  version, description or icon is what you test.
- **Logs are collected, not forwarded.** The harness replaces the Serilog pipeline, `UseMacroDeckLogging`
  included, with `harness.Logs`.

## Testing actions

```csharp
var outcome = await harness.Actions.ExecuteAsync("log-message",
	new Dictionary<string, object?> { ["message"] = "   " });

Assert.That(outcome.Succeeded, Is.False);
Assert.That(outcome.Error, Is.Not.Null);
```

Every client call returns a `CapabilityInvocationOutcome`: `Succeeded`, `Error`, `Data` (read it with
`DataAs<T>()`), `CorrelationId` and `Elapsed`. The harness enforces the invocation deadline but skips
concurrency limiting and idempotency replay. `harness.Actions` also has `GetOptionsAsync`,
`GetActionStateAsync` and `GetActionIconAsync`.

## Testing variables

```csharp
await harness.Actions.ExecuteAsync("ring", new Dictionary<string, object?>());

var reading = (await harness.Variables.GetAsync("rings")).DataAs<VariableReadingDto>();
Assert.That(reading!.Value.Number, Is.EqualTo(1));
```

`GetAsync` and `SetAsync` take the local id, not the variable name. `DiscoverAsync`, `ResolveAsync` and
`SubscribeAsync` cover [the catalog](/features/variables/#the-variable-catalog); a push-capable catalog
is attached to `harness.Context.VariableValues` during `InitializeIntegrationsAsync`.

## Testing events

```csharp
await harness.Actions.ExecuteAsync("ring", new Dictionary<string, object?>());

var published = harness.Context.Events.Published.Single();
Assert.That(published.EventId, Is.EqualTo("rang"));
Assert.That(published.Parameters!.Value.GetProperty("count").GetInt32(), Is.EqualTo(1));
```

`FakeEventPublisher` records every `Publish` in order and never throws, matching the real publisher's
fire-and-forget contract. Parameters are serialized with the protocol's own JSON options, so `Parameters`
is what the host would receive.

## Testing configuration

```csharp
await using var harness = CreateHarness();
var entry = harness.Context.Config.AddEntry("Front door");
harness.Context.Config.SeedString(entry, "room", "Hallway");

await harness.InitializeIntegrationsAsync();
```

Seed entries before `InitializeIntegrationsAsync` to test what the integration does with an existing
configuration; `SeedSecret` does the same for secrets. To drive the [setup flow](/features/setup-flows/)
itself, use `harness.ConfigFlow.StartAsync`, `SubmitAsync` and `AbandonAsync`.

## Testing log output

```csharp
Assert.That(harness.Logs.WithProperty("Room", "Hallway"), Has.Count.EqualTo(1));
Assert.That(harness.Logs.AtLeast(LogLevels.Warning), Is.Empty);
```

Property values are rendered as strings. `WaitForAsync` waits for a line logged from background work.

## Testing devices

```csharp
var devices = new FakeDeviceProviderContext();
var provider = new LightpadProvider();

await provider.InitializeAsync(devices);
var first = devices.AssignedIdOf("pad-1");
await provider.InitializeAsync(devices);

Assert.That(devices.AssignedIdOf("pad-1"), Is.EqualTo(first));
```

`FakeDeviceProviderContext` keeps the host's identity rules: registering again under a known
provider-local id is the same device, and unregistering keeps the device and only takes it offline.
`OpenSession` hands your provider a `FakeDeviceSession` to push surfaces to and read interactions from.
`InitializeIntegrationsAsync` does not initialize device providers, so call `InitializeAsync` yourself.

## Time and waiting

```csharp
harness.Clock.Advance(TimeSpan.FromSeconds(30));
await Wait.UntilAsync(() => harness.Context.Events.Published.Count > 0, because: "the poll should fire");
```

Advance the manual clock instead of sleeping. `Wait.UntilAsync` throws `PluginTestTimeoutException` at
its deadline instead of hanging.

## Protocol and process tests

| Tool | Use it for |
| --- | --- |
| `PluginTestHarness` | Everything above: fast, in process, no socket. Start here. |
| `MacroDeckTestHost.HostAsync` | The real protocol over loopback, plugin in process: serialization, reconnection, cancellation, timeouts, host callback round trips. |
| `MacroDeckTestHost.LaunchAsync` | A built executable or packed `.macroDeckPlugin`: startup, graceful shutdown, environment handoff, manifest loading. Keep these few. |

`PluginTestHarness.ProblemsOf(configure)` returns every configuration problem without throwing.

## The conformance run

```bash
macrodeck-plugin test --project src/Demo
```

```text
Macro Deck plugin conformance report (suite 1.2.0)
Plugin: com.example.demo 1.0.0
Passed: 25, Failed: 0, Skipped: 24
Conformant: yes
```

The [conformance suite](/reference/conformance/) checks the generic plugin contract, so do not repeat it
in your own tests. Test what is specific to your plugin, with fakes only at real external boundaries
(provider APIs, the file system). Options, filters and exit codes are in
[`macrodeck-plugin test`](/cli/test/).

## See also

- [Conformance suite](/reference/conformance/) - every check and the report format.
- [Logging and health](/features/logging/) - what the lines you assert on look like in production.
- [Sample plugins](/introduction/samples-and-template/) - complete test projects.
- [Debugging](/guides/debugging/) - run the plugin under an IDE against the stub or a real host.
