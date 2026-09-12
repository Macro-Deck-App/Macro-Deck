---
title: Samples and template
description: What the plugin template generates, which sample plugins exist and what each one demonstrates, and how to run one.
---

Start from the template; read the samples. Both live in their own repositories, not in the Macro Deck
repository.

| Repository | Use it when |
| --- | --- |
| [Macro-Deck-Plugin-Template](https://github.com/Macro-Deck-App/Macro-Deck-Plugin-Template) | You are starting a plugin. |
| [Macro-Deck-Sample-Plugins](https://github.com/Macro-Deck-App/Macro-Deck-Sample-Plugins) | You want to see a capability implemented end to end. |

## The template

```bash
macrodeck-plugin new --name "My Plugin" --id com.example.my-plugin --publisher "Example" --yes
```

```text
MyPlugin/
├── MyPlugin.slnx
├── Directory.Build.props        net10.0, nullable, analyzers for every project
├── Directory.Packages.props     one MacroDeckSdkVersion for every Macro Deck package
├── NuGet.config                 nuget.org plus an empty local-feed/
├── src/MyPlugin/
│   ├── manifest.json
│   ├── macrodeck-build.json     one self-contained publish per platform
│   ├── MyPlugin.csproj
│   ├── Program.cs               CreatePlugin, logging, localization, one integration
│   ├── PluginIntegration.cs     IPluginIntegration with one action
│   ├── LogMessageAction.cs      the example action - replace it
│   ├── Localization/Strings.resx
│   ├── Assets/icon.svg          replace with your icon
│   └── Properties/launchSettings.json   "Macro Deck - Real Host" debug profile
└── tests/MyPlugin.Tests/        NUnit tests on MacroDeck.Plugin.Testing
```

Every file in `src/MyPlugin/` is explained in [Project setup](/introduction/manual-setup/). `new`
installs the template if needed, fills in publisher, license, repository and homepage, and writes only
the platforms you pick - see [`macrodeck-plugin new`](/cli/new/).

Without the CLI, use the template directly:

```bash
dotnet new install "MacroDeck.Plugin.Templates@*-*"
dotnet new macrodeck-plugin -n Acme.LightControl -o Acme.LightControl \
  --pluginId com.acme.light-control --pluginName "Acme Light Control"
```

`@*-*` picks the newest prerelease template. `-o` creates the directory; leave it out only when you are
already in the project directory. Cloning the template repository and renaming by hand also works; its
README has the rename checklist.

## The samples

Each sample is one self-contained plugin, laid out exactly as `new` scaffolds a project. None needs an
external service, credentials or network access.

| Sample | Demonstrates |
| --- | --- |
| [Weather](https://github.com/Macro-Deck-App/Macro-Deck-Sample-Plugins/tree/main/src/MacroDeck.SampleWeatherPlugin) | The smallest complete plugin: plain and dynamic-options actions, read-only and writable variables, an event, a one-step config flow, a weather provider, a second language (`de`). |
| [Music player](https://github.com/Macro-Deck-App/Macro-Deck-Sample-Plugins/tree/main/src/MacroDeck.SampleMusicPlayerPlugin) | Transport, artwork, catalogue browsing, output devices, two instances with different capabilities, dynamic event options, action interaction pickers. |
| [REST API](https://github.com/Macro-Deck-App/Macro-Deck-Sample-Plugins/tree/main/src/MacroDeck.SampleRestApiPlugin) | A typed `HttpClient` through DI, a multi-step config flow with a secret and an OAuth branch, integration issues, notifications, API-backed variables and options. |
| [Virtual profile](https://github.com/Macro-Deck-App/Macro-Deck-Sample-Plugins/tree/main/src/MacroDeck.SampleVirtualProfilePlugin) | A plugin-owned virtual profile with widget interactions, pushed variable updates, deck navigation, widget appearance, scripts and notifications. |

The samples README has the full capability-by-sample matrix.

## Run a sample

```bash
git clone https://github.com/Macro-Deck-App/Macro-Deck-Sample-Plugins
cd Macro-Deck-Sample-Plugins
dotnet build
dotnet test
```

```bash
macrodeck-plugin run --project src/MacroDeck.SampleWeatherPlugin --stub-host
```

Against a disposable stub host - no Macro Deck needed. To debug one against the desktop app, start its
**Macro Deck - Real Host** launch profile - see [Debugging plugins](/guides/debugging/).

```bash
cd src/MacroDeck.SampleWeatherPlugin
macrodeck-plugin build --output ../../artifacts
```

Builds and packs every platform, like your own plugin.

## Not in the samples

Two things ship as packages in the Macro Deck SDK instead - reference them from your own test project:

- **`MacroDeck.Plugin.Testing`** - loopback test host, fakes and assertions. See
  [Testing plugins](/features/testing/).
- **The conformance suite** - `macrodeck-plugin test`, with stable check ids for CI. See
  [Conformance](/reference/conformance/).

## See also

- [Quickstart](/introduction/quickstart/) - create and run a plugin from the template.
- [Project setup](/introduction/manual-setup/) - every generated file, written by hand.
- [Plugin hosting](/reference/plugin-hosting/) - the builder API the template uses.
- [Contributing an integration](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/development/contributing-integrations.md) -
  for an in-process integration shipped with the host rather than a plugin.
