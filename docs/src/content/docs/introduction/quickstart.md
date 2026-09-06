---
title: Quickstart
description: Create and run your first Macro Deck plugin against a disposable stub host in about five minutes.
---

In about five minutes, you will create a plugin from the official template and connect it to a real,
disposable stub host. You do not need Macro Deck installed. The finish line is a CLI message beginning
with `Session established`.

## Prerequisites

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). It includes the ASP.NET
Core shared framework used by the plugin and the stub host.

## 1. Install the developer CLI

```bash
dotnet tool install --global MacroDeck.Plugin.Cli --prerelease
```

`--prerelease` is required while Macro Deck 3 packages are published as previews.

## 2. Create a plugin

```bash
macrodeck-plugin new
```

This walks a short wizard - display name, plugin id, publisher, target platforms - then installs the
official plugin template if it is not already present and scaffolds the project. To follow along, answer
`Acme Light Control` for the name, `Acme.LightControl` for the project name, and accept the rest of the
offered defaults.

To skip the wizard entirely, pass the values instead:

```bash
macrodeck-plugin new --name "Acme Light Control" --id com.acme.light-control \
  --publisher "Acme" --project-name Acme.LightControl --yes
```

`--project-name` is optional - it defaults to the display name with the spaces removed
(`AcmeLightControl`) - and is passed here only so the paths below match.

Either way you get an `Acme.LightControl` directory containing a plugin project, a test project, a
manifest and an icon. See [`new`](/cli/new/) for every option.

<details>
<summary>Without the CLI</summary>

The template is also usable directly, though you then fill in the publisher, license and target
platforms yourself:

```bash
dotnet new install "MacroDeck.Plugin.Templates@*-*"
dotnet new macrodeck-plugin -n Acme.LightControl --pluginId com.acme.light-control --pluginName "Acme Light Control"
```

`@*-*` selects the newest published template version. The floating version is needed while Macro Deck
3 is in preview, because `dotnet new install` otherwise prefers stable packages.

</details>

## 3. Run the plugin

```bash
cd Acme.LightControl
macrodeck-plugin run --project src/Acme.LightControl --stub-host
```

`--stub-host` is what makes this work without Macro Deck installed: without it, `run` connects to the
running desktop host instead. The command builds the project, launches the plugin and connects it to the disposable stub host using
the same registration, session and WebSocket implementation as a real host. When the connection is
ready, look for:

```text
Session established ...
```

That is the complete first run. Press <kbd>Ctrl</kbd>+<kbd>C</kbd> to stop the plugin.

## What the template gives you

The generated project is intentionally small:

- `src/Acme.LightControl/Program.cs` registers and runs the integration.
- `src/Acme.LightControl/PluginIntegration.cs` is where you add actions and other capabilities.
- `src/Acme.LightControl/manifest.json` contains the plugin identity and entrypoints.
- `src/Acme.LightControl/Assets/icon.svg` is the icon to replace.
- `tests/Acme.LightControl.Tests/` contains the starting test project.

See [Samples and template](/introduction/samples-and-template/) for worked plugins and the
[SDK reference](/sdk/) for the capability interfaces you can implement.

## Next steps

- Add and trigger a button action in [Your first action](/introduction/first-action/).
- Learn how the generated host, manifest and integration fit together in
  [Plugin hosting](/sdk/hosting/).
- Explore more capabilities in the [sample plugins](/introduction/samples-and-template/).
- Test plugin behavior with the [testing guide](/sdk/testing/).
- Validate, package and conformance-test a distributable build with
  [the plugin CLI](/cli/) and the [conformance guide](/sdk/conformance/).
- Set breakpoints and connect to a real Macro Deck host with the
  [debugging guide](/guides/debugging/).
- If the first run does not reach `Session established`, use
  [Troubleshooting](/guides/troubleshooting/).

If you prefer to assemble every project file yourself, follow
[Create a plugin manually](/introduction/manual-setup/). The
[template repository](https://github.com/Macro-Deck-App/Macro-Deck-Plugin-Template) also documents
the equivalent clone-and-rename route.
