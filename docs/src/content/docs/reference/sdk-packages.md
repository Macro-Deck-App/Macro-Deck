---
title: SDK overview
description: The packages a Macro Deck plugin builds against, the integration model they share, and where each area is documented.
---

Small packages, so a plugin depends only on what it uses. Every public member is documented in XML
docs and IntelliSense; every package is a compatibility commitment - see the
[compatibility policy](/policies/compatibility/). Package versions follow the Macro Deck release they were
built with; each package's README is its NuGet overview.

## Packages

| Package | Install | For | Docs |
| --- | --- | --- | --- |
| `MacroDeck.Plugin.Hosting` | `dotnet add package MacroDeck.Plugin.Hosting` | Running an out-of-process plugin on ASP.NET Core; brings `MacroDeck.Sdk`. | [Plugin hosting](/reference/plugin-hosting/) |
| `MacroDeck.Sdk` | `dotnet add package MacroDeck.Sdk` | Integration contracts: lifecycle, actions, capabilities, variables, events, profiles, decks, widgets, notifications, config flows, UI session providers. | [Capabilities](/features/) |
| `MacroDeck.Localization` | `dotnet add package MacroDeck.Localization` | Localization keys, deferred `LocalizedString`/`LocalizedText`, the culture fallback chain and the reusable `MacroDeckStrings` catalog. | [Localization](/features/localization/) |
| `MacroDeck.Plugin.Analyzers` | `dotnet add package MacroDeck.Plugin.Analyzers` | Compile-time diagnostics, and the source generator that turns `Localization/*.resx` into a typed API. | [Analyzers](/reference/analyzers/) |
| `MacroDeck.Plugin.Serilog` | `dotnet add package MacroDeck.Plugin.Serilog` | Forwarding Serilog events to the host log. | [Logging and health](/features/logging/) |
| `MacroDeck.Plugin.Testing` | `dotnet add package MacroDeck.Plugin.Testing` | Test host, fakes and the conformance suite. | [Testing plugins](/features/testing/) |
| `MacroDeck.Plugin.Cli` | `dotnet tool install --global MacroDeck.Plugin.Cli --prerelease` | The `macrodeck-plugin` tool. | [Plugin CLI](/cli/) |
| `MacroDeck.Ui` | `dotnet add package MacroDeck.Ui` | The Macro Deck UI framework: declarative C# DSL and reactive runtime over `MacroDeck.Ui.Model`. | [Macro Deck UI](/ui/) |
| `MacroDeck.Ui.Testing` | `dotnet add package MacroDeck.Ui.Testing` | Headless testing of Macro Deck UI views. | [Macro Deck UI](/ui/) |
| `MacroDeck.Ui.Model` | `dotnet add package MacroDeck.Ui.Model` | Transport-neutral UI tree, events, patches, resources and capability negotiation - the wire contract under the UI framework. | [Macro Deck UI](/ui/) |
| `MacroDeck.Plugin.Packaging` | `dotnet add package MacroDeck.Plugin.Packaging` | The manifest and `.macroDeckPlugin` package model. | [Manifest](/reference/manifest/) |
| `MacroDeck.Plugin.Protocol` | `dotnet add package MacroDeck.Plugin.Protocol` | Low-level plugin wire contracts. Most plugins use `MacroDeck.Plugin.Hosting` instead. | [Plugin protocol](/reference/protocol/) |
| `MacroDeck.Signing` | `dotnet add package MacroDeck.Signing` | Ed25519 package signing and verification shared by the CLI, host and Platform. | [Security model](/policies/security/) |

Only `-preview` versions are published before 3.0, so add `--prerelease` to `dotnet add package` too.

## First use

### `MacroDeck.Plugin.Hosting`

```csharp
var plugin = MacroDeckPlugin.CreatePlugin(args)
    .RegisterIntegration<PluginIntegration>()
    .Build();

await plugin.RunAsync();
```

### `MacroDeck.Sdk`

```csharp
public sealed class PluginIntegration(ILogger logger) : IPluginIntegration
{
    public IReadOnlyList<IActionDefinition> Actions { get; } = [new LogMessageAction(logger)];
    public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;
    public Task ShutdownAsync() => Task.CompletedTask;
}
```

### `MacroDeck.Localization` and `MacroDeck.Plugin.Analyzers`

```xml
<PackageReference Include="MacroDeck.Localization" />
<PackageReference Include="MacroDeck.Plugin.Analyzers" PrivateAssets="all" />
```

```csharp
public LocalizedText Name => Strings.Actions.SetBrightness.Name(); // generated from Localization/Strings.resx
new Text(MacroDeckStrings.Common.Save())                           // the shared catalog
```

`PrivateAssets="all"` keeps the analyzers a build-time-only dependency.

### `MacroDeck.Plugin.Serilog`

```csharp
var plugin = MacroDeckPlugin.CreatePlugin(args)
    .UseMacroDeckLogging(cfg => cfg.MinimumLevel.Debug())
    .RegisterIntegration<PluginIntegration>()
    .Build();
```

### `MacroDeck.Plugin.Testing`

```csharp
await using var harness = PluginTestHarness.Create(b => b.RegisterIntegration<PluginIntegration>());
await harness.InitializeIntegrationsAsync();

var outcome = await harness.Actions.ExecuteAsync("log-message",
    new Dictionary<string, object?> { ["message"] = "Hello" });
```

### `MacroDeck.Plugin.Cli`

```bash
dotnet tool install --global MacroDeck.Plugin.Cli --prerelease
macrodeck-plugin new --name "Hue Lights" --id com.example.hue-lights
macrodeck-plugin build
```

### `MacroDeck.Ui`

```csharp
private readonly UiState<string> _apiKey = new(string.Empty);

private UiFlow Build() => new()
{
    Key = "setup",
    Children = [new UiStep { Key = "credentials", Children = [new UiStringInput { Key = "apiKey", Binding = Bind.To(_apiKey) }] }],
};
```

### `MacroDeck.Ui.Testing`

```csharp
var host = UiTestHost.Render(view);
host.ById("apiKey").Change("k-1");
await host.SettleAsync();
Assert.That(host.ById("apiKey").Text(UiConfigProperties.Value), Is.EqualTo("k-1"));
```

### `MacroDeck.Ui.Model`

```csharp
var node = new UiNode { Id = "title", Type = "text" };
string json = UiCanonicalJson.Serialize(node);
```

### `MacroDeck.Plugin.Packaging`

```csharp
IPluginManifestReader reader = new PluginManifestReader();
var result = reader.Read("manifest.json", expectedPluginId: "com.example.hue-lights", expectedVersion: "1.0.0");
if (!result.Success) Console.WriteLine($"{result.Error}: {result.ErrorMessage}");
```

### `MacroDeck.Plugin.Protocol`

```csharp
var outcome = ProtocolVersionNegotiator.Negotiate(new ProtocolVersionRange { Minimum = 1, Maximum = 3 });
if (outcome.Succeeded) Console.WriteLine($"Speaking protocol {outcome.NegotiatedVersion}");
```

### `MacroDeck.Signing`

```csharp
var result = await PackageVerifier.VerifyAsync("HueLights.macroDeckPlugin", new PluginManifestReader());
Console.WriteLine(result.Success ? $"Signed by {result.CertificateId}" : result.Message);
```

Store artifacts are signed by the Creator Portal; no signing key reaches a plugin author. See
[Publishing to the Store](/guides/publishing/).

## Core integration model

An integration exposes actions and opts into more by implementing capability interfaces. Out-of-process
plugins use the same contracts through `MacroDeck.Plugin.Hosting`.

| Area | Namespace |
| --- | --- |
| Actions | `MacroDeck.Sdk.Actions` - `IStateProviderActionDefinition` lets a configured action instance supply an Action Button's states and current state |
| Configuration flows | `MacroDeck.Sdk.ConfigFlow` |
| Variables | `MacroDeck.Sdk.Variables` |
| Events | `MacroDeck.Sdk.Events` |
| Profiles and deck navigation | `MacroDeck.Sdk.Profiles`, `MacroDeck.Sdk.Decks` |
| Widgets | `MacroDeck.Sdk.Widgets` - states addressed by stable id; `WidgetStateSelector` is deprecated, see [migrations](/policies/migrations/) |
| Music and weather providers | `MacroDeck.Sdk.MusicPlayer`, `MacroDeck.Sdk.Weather` |
| Integration issues and logging | `MacroDeck.Sdk.Issues`, `MacroDeck.Sdk.Logging` |
| UI session providers | `MacroDeck.Sdk.Ui` - `IUiProvider` and `IUiSession` in `MacroDeck.Ui.Model` types, so a provider needs no UI framework dependency. See [Macro Deck UI](/ui/). |

Use IntelliSense for individual member contracts.

## Logging

Use the SDK logging abstractions for integration diagnostics, and `MacroDeck.Plugin.Serilog` to forward an
out-of-process plugin's Serilog events into the host log. Never log credentials or other reusable secrets.

## Conditional fields

A configuration input can be conditionally visible while staying part of the form state - unlike
structurally omitting an element from a Macro Deck UI tree. See
[Config and action flows](/features/setup-flows/) and [State and bindings](/ui/concepts/state-and-bindings/).

## Capability identity

Declared capability ids are local. Macro Deck qualifies them with the owning integration or plugin identity
before registration; do not build qualified ids yourself where an API asks for a local id.

## In this section

| Page | What it covers |
| --- | --- |
| [Plugin hosting](/reference/plugin-hosting/) | Builder API, dependency injection, registration modes, lifecycle, reserved routes. |
| [Capabilities](/features/) | Every capability interface and how to choose. |
| [Config and action flows](/features/setup-flows/) | The two things Macro Deck calls a flow. |
| [Macro Deck UI](/ui/) | Authoring, state and bindings, components, serving, headless testing. |
| [Localization](/features/localization/) | `Localization/*.resx`, the typed API, the fallback chain, `MDLOC` diagnostics. |
| [Logging and health](/features/logging/) | Serilog forwarding and the health endpoints. |
| [Authentication](/reference/authentication/) | Credentials, session tokens, pairing. |
| [Testing plugins](/features/testing/) | The harness, the protocol host, what to assert. |
| [Conformance suite](/reference/conformance/) | Framework-agnostic contract checks. |
| [Analyzers](/reference/analyzers/) | Compile-time diagnostics and suppression. |
| [Capability parity](/reference/capability-parity/) | Where out-of-process differs from in-process. |

Beyond the SDK: the [plugin CLI](/cli/) builds, validates and packages; the [manifest](/reference/manifest/)
and [protocol](/reference/protocol/) references document the formats underneath.
