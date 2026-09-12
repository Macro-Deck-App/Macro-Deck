---
title: Project setup
description: Every file of a Macro Deck plugin project, written by hand - the project file, manifest, entrypoint, integration and build recipe - and how to run it.
---

A plugin is a .NET 10 console project with a `manifest.json` beside it; this page builds one by hand, or
explains what [`macrodeck-plugin new`](/cli/new/) generated for you.

## The files

```text
MyPlugin/
├── Assets/
│   └── icon.svg            the plugin icon, referenced by manifest.json
├── MyIntegration.cs        your capabilities
├── MyPlugin.csproj         a console project with the Macro Deck packages
├── Program.cs              starts the plugin
├── macrodeck-build.json    how macrodeck-plugin build builds each platform
└── manifest.json           who the plugin is
```

`new` generates the same project under `src/MyPlugin/`, plus a solution, a test project, a
`Localization/` resource set and central package versions.

## Project file

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
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

- **`Microsoft.NET.Sdk` plus the ASP.NET Core framework reference**, not `Microsoft.NET.Sdk.Web`: the
  hosting package builds on ASP.NET Core, but a plugin is a headless process.
- **`3.0.0-*`** picks the newest Macro Deck 3 preview. Pin an exact version for reproducible builds.
- **`MacroDeck.Plugin.Analyzers`** is optional but recommended: it reports invalid declarations at
  build time. See [Analyzers](/reference/analyzers/).
- **Both `Content` items are required.** The SDK reads `manifest.json` from the content root at startup
  and resolves the icon against it.

## manifest.json

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
    "win-x64": { "executable": "runtimes/win-x64/MyPlugin.exe" },
    "osx-arm64": { "executable": "runtimes/osx-arm64/MyPlugin" },
    "linux-x64": { "executable": "runtimes/linux-x64/MyPlugin" }
  }
}
```

| Field | Rule |
| --- | --- |
| `manifestVersion` | Always `1`. |
| `id` | Reverse-domain, lowercase, at least two segments: `com.example.my-plugin`. |
| `name` | Display name, 1-128 characters. |
| `version` | SemVer 2.0. |
| `entrypoints` | One entry per runtime identifier you have tested; `executable` is relative to the package root. |

Identity lives only here - the hosting builder has no `WithId`, `WithName` or `WithVersion`. The
`runtimes/<rid>/` paths are where [`build`](/cli/build/) stages each self-contained publish. Before
publishing you also need `publisher`, `license`, `repository` and `compatibility`; `build` warns about
each one that is missing. Every field is in the [manifest reference](/reference/manifest/).

## Program.cs

```csharp
using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Serilog;

var plugin = MacroDeckPlugin.CreatePlugin(args)
    .UseMacroDeckLogging()
    .RegisterIntegration<MyIntegration>()
    .Build();

await plugin.RunAsync();
```

- `RegisterIntegration<T>()` registers the integration and every capability interface it implements.
- `Build()` checks the manifest identity, duplicate capability ids, reserved routes and the DI graph,
  and reports every problem at once.
- `UseMacroDeckLogging()` forwards the plugin's logs to the Macro Deck log viewer - see
  [Logging](/features/logging/).

## The integration class

```csharp
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;

public sealed class MyIntegration : IPluginIntegration
{
    public IReadOnlyList<IActionDefinition> Actions { get; } = [];

    public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

    public Task ShutdownAsync() => Task.CompletedTask;
}
```

The host reads `Actions` (and every other capability list) before `InitializeAsync` runs, so build them
in the constructor without I/O. Connect to devices or services in `InitializeAsync`, release them in
`ShutdownAsync`. Fill `Actions` in with [Your first action](/introduction/first-action/).

## Build recipe

`macrodeck-build.json`, beside the manifest - one target per entrypoint:

```json
{
  "version": 1,
  "targets": {
    "win-x64": {
      "executable": "dotnet",
      "arguments": ["publish", "MyPlugin.csproj", "-c", "Release", "-r", "win-x64",
                    "--self-contained", "true", "-o", "bin/publish/win-x64"],
      "output": "bin/publish/win-x64"
    },
    "osx-arm64": {
      "executable": "dotnet",
      "arguments": ["publish", "MyPlugin.csproj", "-c", "Release", "-r", "osx-arm64",
                    "--self-contained", "true", "-o", "bin/publish/osx-arm64"],
      "output": "bin/publish/osx-arm64"
    },
    "linux-x64": {
      "executable": "dotnet",
      "arguments": ["publish", "MyPlugin.csproj", "-c", "Release", "-r", "linux-x64",
                    "--self-contained", "true", "-o", "bin/publish/linux-x64"],
      "output": "bin/publish/linux-x64"
    }
  }
}
```

A manifest platform without a target fails the build. The format is in [`build`](/cli/build/).

## Services and configuration

The builder exposes the usual ASP.NET Core `Services`, `Configuration`, `Logging` and `Environment`.
Register services before `Build()`, then take them in the integration's constructor:

```csharp
using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Serilog;
using Microsoft.Extensions.DependencyInjection;

var builder = MacroDeckPlugin.CreatePlugin(args)
    .UseMacroDeckLogging()
    .RegisterIntegration<MyIntegration>();

builder.Services.AddHttpClient<WeatherClient>(client =>
    client.BaseAddress = new Uri("https://api.example.com/"));
builder.Services.Configure<WeatherOptions>(builder.Configuration.GetSection("Weather"));

var plugin = builder.Build();
await plugin.RunAsync();
```

```csharp
public sealed class MyIntegration(WeatherClient weather, IOptions<WeatherOptions> options)
    : IPluginIntegration
{
    // ...
}
```

`builder.WebApplicationBuilder` is there as an escape hatch; prefer the Macro Deck APIs for
registration and lifecycle. Runtime behaviour - registration modes, lifecycle, reserved routes - is in
[Plugin hosting](/reference/plugin-hosting/).

## Run it

```bash
dotnet tool install --global MacroDeck.Plugin.Cli --prerelease
```

```bash
macrodeck-plugin run --project MyPlugin.csproj --stub-host
```

```text
Started a disposable stub host at http://127.0.0.1:52484.
...
Session established (negotiated plugin protocol v3).
```

A real in-process host, no Macro Deck install needed. Drop `--stub-host` to connect to the running
desktop app instead - see [`run`](/cli/run/).

```bash
macrodeck-plugin build --output ../artifacts
macrodeck-plugin validate --artifact ../artifacts/com.example.my-plugin-1.0.0.macroDeckPlugin
```

Builds every platform, packs one `.macroDeckPlugin` and checks it. Validate the artifact, not the
source manifest: the `runtimes/` entrypoints only exist once `build` has staged them.

Never zip the output by hand - `build` and [`pack`](/cli/pack/) validate the manifest and write the
`files[]` digests a hand-made ZIP lacks. For breakpoints against a real host, see
[Debugging plugins](/guides/debugging/); never commit a Developer token.

## See also

- [Quickstart](/introduction/quickstart/) - the same project, generated.
- [Samples and template](/introduction/samples-and-template/) - complete plugins to read.
- [Your first action](/introduction/first-action/) - add behaviour to `MyIntegration`.
- [Manifest reference](/reference/manifest/) - every field.
- [Testing plugins](/features/testing/) - test the integration without a host.
