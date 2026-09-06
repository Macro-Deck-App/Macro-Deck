---
title: Manual setup
description: Assemble a Macro Deck plugin by hand, without the template or CLI scaffolding, then build and run it against a stub or real host.
---

This is the explicit manual alternative to the [Quickstart](/introduction/quickstart/). It creates the
same small plugin without cloning the template or asking a tool to scaffold files. Use it when you want
to understand every project component or add plugin hosting to an existing console application.

You need the .NET 10 SDK. Create this layout in an empty directory, replacing `MyPlugin` with your own
project name if necessary:

```text
MyPlugin/
├── Assets/
│   └── icon.svg
├── MyIntegration.cs
├── MyPlugin.csproj
├── Program.cs
├── SayHelloAction.cs
└── manifest.json
```

## Create the project file

A plugin is a normal `net10.0` console project. It references the ASP.NET Core shared framework because
`MacroDeck.Plugin.Hosting` builds on ASP.NET Core; it is not a `Microsoft.NET.Sdk.Web` project.

Create `MyPlugin.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
        <UseAppHost>false</UseAppHost>
    </PropertyGroup>

    <ItemGroup>
        <FrameworkReference Include="Microsoft.AspNetCore.App" />
        <PackageReference Include="MacroDeck.Plugin.Hosting" Version="3.0.0-*" />
        <PackageReference Include="MacroDeck.Plugin.Serilog" Version="3.0.0-*" />
        <PackageReference Include="MacroDeck.Plugin.Analyzers" Version="3.0.0-*" PrivateAssets="all" />
    </ItemGroup>

    <ItemGroup>
        <Content Include="manifest.json" CopyToOutputDirectory="PreserveNewest" />
        <Content Include="Assets/icon.svg" CopyToOutputDirectory="PreserveNewest" />
    </ItemGroup>

</Project>
```

`3.0.0-*` selects the newest published Macro Deck 3 preview; pin an exact version when you need
reproducible dependency updates. The analyzer package is optional but strongly recommended: it
catches invalid declarations during the build and confirms generated capability metadata. The two
`Content` items are not optional for this layout. The hosting SDK reads `manifest.json` from the
content root and resolves the icon relative to that root, so both files must be copied beside the
build output. See the [analyzer reference](/sdk/analyzers/) for the analyzer and
source-generator rules.

## Add the manifest and icon

Create an SVG icon at `Assets/icon.svg`, then create `manifest.json` beside the project file:

```json
{
  "$schema": "https://schemas.macro-deck.app/plugin-manifest-v1.schema.json",
  "manifestVersion": 1,
  "id": "com.example.my-plugin",
  "name": "My Plugin",
  "version": "1.0.0",
  "description": "What the plugin does.",
  "icon": "Assets/icon.svg",
  "entrypoints": {
    "win-x64": {
      "executable": "MyPlugin.dll",
      "runtime": { "kind": "FrameworkDependent", "dotnetVersion": "10.0" }
    },
    "osx-arm64": {
      "executable": "MyPlugin.dll",
      "runtime": { "kind": "FrameworkDependent", "dotnetVersion": "10.0" }
    },
    "linux-x64": {
      "executable": "MyPlugin.dll",
      "runtime": { "kind": "FrameworkDependent", "dotnetVersion": "10.0" }
    }
  }
}
```

Change the reverse-domain `id`, display text, and entrypoint names for your plugin. Keep identity in the
manifest; the hosting builder has no `WithId`, `WithName`, or `WithVersion` calls. Declare only the
runtime identifiers you have tested. This minimal project is framework-dependent: `UseAppHost=false`
keeps the Release output platform-neutral, every declared platform launches the same `MyPlugin.dll`,
and the target machine must provide .NET 10. For a self-contained artifact, publish separately for each
RID and point each entrypoint at its matching native executable. The
[manifest reference](/reference/manifest/) documents every field, entrypoint mode, identifier rule,
and the published JSON Schema.

## Add one action

Create `SayHelloAction.cs`:

```csharp
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

internal sealed class SayHelloAction : IActionDefinition
{
    public string Id => "say-hello";

    public LocalizedText Name => "Say hello";

    public LocalizedText Description => "Writes a greeting to the plugin's log.";

    public IReadOnlyList<ActionParameter> Parameters { get; } =
    [
        new ActionParameter { Name = "who", Type = ActionParameterType.String, Label = "Who", Required = true }
    ];

    public IActionExecutor CreateExecutor() => new Executor();

    private sealed class Executor : IActionExecutor
    {
        public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
        {
            var who = context.Parameters.TryGetValue("who", out var value) ? value?.ToString() : null;

            if (string.IsNullOrWhiteSpace(who))
            {
                return Task.FromResult(ActionResult.Failed(ActionErrorCodes.InvalidParameter, "No name was given."));
            }

            Console.WriteLine($"Hello, {who}!");
            return ActionResult.SucceededTask;
        }
    }
}
```

An executor reports a stable failure when it cannot complete the action; it must not silently return
success. Long-running work should also honor `context.CancellationToken`. See the
[SDK reference](/sdk/) for actions, parameters, results, and the other capability interfaces.

## Add the integration

Create `MyIntegration.cs`:

```csharp
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;

public sealed class MyIntegration : IPluginIntegration
{
    public IReadOnlyList<IActionDefinition> Actions { get; } = [new SayHelloAction()];

    public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

    public Task ShutdownAsync() => Task.CompletedTask;
}
```

Keep the capability collections complete and side-effect free during construction. The host builds the
capability catalogue before `InitializeAsync` runs, so connect to external services in `InitializeAsync`
and release them in `ShutdownAsync`. Constructor injection is available for services such as
`IHttpClientFactory`, options, and logging.

## Add the entrypoint

Create `Program.cs`:

```csharp
using MacroDeck.Plugin.Hosting;

var plugin = MacroDeckPlugin.CreatePlugin(args)
    .UseMacroDeckLogging()
    .RegisterIntegration<MyIntegration>()
    .Build();

await plugin.RunAsync();
```

`Build()` validates the local plugin configuration and reports all discovered problems together.
`UseMacroDeckLogging()` comes from `MacroDeck.Plugin.Serilog` and forwards the plugin's logs to the
Macro Deck log viewer while connected. The [hosting guide](/sdk/hosting/) covers configuration,
dependency injection, registration, and reserved routes; the [logging guide](/sdk/logging/) covers
log forwarding.

## Build it

From the project directory, restore packages and compile the plugin:

```bash
dotnet build
```

The output directory must now contain the framework-dependent launch target `MyPlugin.dll`,
`manifest.json`, and `Assets/icon.svg`. If either content file is absent, fix the `Content` entries
before trying to run the plugin.

## Choose a development host

For a first run without installing or configuring Macro Deck, install the developer CLI and launch the
project against its disposable stub host:

```bash
dotnet tool install --global MacroDeck.Plugin.Cli --prerelease
macrodeck-plugin run --project MyPlugin.csproj --stub-host
```

The stub uses the real registration, session, and WebSocket implementation and streams the plugin's
console output. It is the shortest way to verify this manually assembled project.

To set breakpoints while connecting to a running Macro Deck desktop host, follow
[Debugging plugins](/guides/debugging/). That guide covers pressing F5 and approving the interactive
pairing prompt, the one-time Developer-token enrollment for headless runs, safe `launchSettings.json`
profiles for Visual Studio or Rider, and the child-process attach workflow used when the CLI launches
the plugin. Do not commit a Developer token or paste one into published documentation.

## Package with the CLI

Manual setup stops at the source tree. Do **not** hand-zip the build output to create a
`.macroDeckPlugin` file. Packaging deliberately stays a CLI operation because `pack`:

- validates the manifest with the host's real reader and the published JSON Schema;
- checks the permission vocabulary and artifact safety rules;
- rebuilds `files[]` from the payload that is actually on disk;
- computes the SHA-256 digest for every packaged file.

Create a Release build and pack its output:

```bash
dotnet build -c Release
macrodeck-plugin pack \
  --source bin/Release/net10.0 \
  --output MyPlugin-1.0.0.macroDeckPlugin
```

A hand-built ZIP can look correct while carrying stale or missing `files[]` entries and digests; the
CLI keeps the manifest consistent with the payload and rejects invalid input before writing it. See
[the plugin CLI](/cli/) for `validate`, `pack`, and `inspect`, and the
[`files[]` manifest reference](/reference/manifest/#files) for the integrity contract.

## Next steps

- [Quickstart](/introduction/quickstart/) for the recommended template-first path.
- [Samples and template](/introduction/samples-and-template/) for complete plugin examples.
- [Debugging plugins](/guides/debugging/) for real-host launch profiles and breakpoints.
- [Testing plugins](/sdk/testing/) for loopback integration tests and SDK fakes.
