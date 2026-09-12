---
title: macrodeck-plugin new
description: Scaffold a new plugin project from the official template, interactively or from a script.
---

`new` creates a plugin project from the official
[plugin template](https://github.com/Macro-Deck-App/Macro-Deck-Plugin-Template), with a `manifest.json`
and `macrodeck-build.json` ready for [`build`](/cli/build/).

## Examples

```bash
macrodeck-plugin new
```

Walks an interactive wizard. Use it when starting a plugin by hand.

```bash
macrodeck-plugin new --name "Spotify Controller" --publisher "Example Publisher" \
  --repository https://github.com/example/spotify-controller --yes
```

```text
Created plugin project at '~/src/SpotifyController'.
Manifest: ~/src/SpotifyController/src/SpotifyController/manifest.json
Build configuration: ~/src/SpotifyController/src/SpotifyController/macrodeck-build.json
```

No prompts; the id (`com.example.spotify-controller`), project name and output directory are derived. Use
it from a script or an IDE integration.

```bash
macrodeck-plugin new --name "Spotify Controller" --id com.example.spotify-controller \
  --publisher "Example Publisher" --non-interactive
```

No prompts and no guessing: a missing required value fails instead of being defaulted. Use it in CI.

```text
$ macrodeck-plugin new --name "Spotify Controller" --publisher "Example Publisher" --non-interactive
error missing-required-option: --id must be supplied when running non-interactively.
```

```bash
macrodeck-plugin new --name "Acme Light Control" --publisher Acme --platform win-x64 --yes
```

```text
Created plugin project at '~/src/AcmeLightControl'.
...
warning publication-metadata-missing: 'repository' is required to publish to the Macro Deck plugin ecosystem. It is not required to develop or run this plugin locally.
```

A Windows-only plugin. After scaffolding, `new` warns about each field still missing for publication.

## Options

| Option | Default | Description |
| --- | --- | --- |
| `--name <name>` | prompted | The plugin's display name. Required. |
| `--id <id>` | prompted | The reverse-domain plugin id, checked by the manifest's rule. Required unless `--yes`. |
| `--publisher <name>` | prompted | The publisher's display name. Required. |
| `--description <text>` | `A Macro Deck plugin.` | Written to `manifest.description`. |
| `--repository <url>` | omitted | Absolute `http`/`https` URL. |
| `--homepage <url>` | omitted | Absolute `http`/`https` URL; the wizard offers the repository URL. |
| `--license <spdx>` | `MIT` | Written to `manifest.license` verbatim. |
| `--platform <rid>` | `win-x64`, `osx-arm64`, `linux-x64` | A target runtime identifier; repeat for each platform. |
| `--project-name <name>` | derived from `--name` | The C# project and assembly name. |
| `--output <dir>` | `./<project name>` | Directory to scaffold into; must not exist or be empty. |
| `-y`, `--yes` | off | Never prompt; accept every default the wizard would offer. |
| `--non-interactive` | off | Never prompt; a missing required value is a usage error. |
| `--template-version <v>` | latest prerelease | Install this exact template version. |
| `--skip-template-install` | off | Never probe or install the template; it must already be installed. |
| `--no-restore` | off | Skip the NuGet restore `dotnet new` would run. |

## Prompting

The wizard runs only when stdin is a terminal and neither `--yes` nor `--non-interactive` is given. Options
you pass become the wizard's defaults, so it only fills gaps, then asks `Create plugin? [Y/n]`.

Without a terminal, a missing required value is a `missing-required-option` usage error rather than a
prompt that would hang a CI job.

| | Missing `--id` | Missing `--name` or `--publisher` |
| --- | --- | --- |
| `--yes` | derived from `--name` as `com.example.<kebab-name>` | usage error |
| `--non-interactive` | usage error | usage error |

## Platforms

```bash
macrodeck-plugin new --name "Acme Light Control" --publisher Acme --yes \
  --platform win-x64 --platform win-arm64
```

- `--platform` accepts `win-x64`, `win-arm64`, `osx-arm64`, `osx-x64`, `linux-x64` and `linux-arm64`.
  Anything else fails with `unknown-platform`; selecting none fails with `no-platform-selected`.
- The wizard offers only `win-x64`, `osx-arm64` and `linux-x64`, the platforms Macro Deck ships builds
  for, as a checkbox list (a numbered list on a terminal without ANSI support).
- Only the selected platforms are written to `manifest.entrypoints` and `macrodeck-build.json`.

## Template handling

`new` checks whether the template is installed and only reaches the network if it is not, so later runs
work offline. It installs `MacroDeck.Plugin.Templates@*-*` (the `*-*` picks up `-preview` versions) or
the exact `--template-version`. If the install fails, the error carries the `dotnet` output.

## Generated files

```text
SpotifyController/
  SpotifyController.slnx
  src/SpotifyController/
    manifest.json
    macrodeck-build.json
    SpotifyController.csproj
    Program.cs
    PluginIntegration.cs
    Assets/icon.svg
    Localization/Strings.resx
    ...
  tests/SpotifyController.Tests/
```

`manifest.json` and `macrodeck-build.json` sit in `src/<ProjectName>/`, so run `build` there (or pass
`--source`).

### manifest.json

```json
{
  "id": "com.example.spotify-controller",
  "name": "Spotify Controller",
  "version": "1.0.0",
  "description": "A Macro Deck plugin.",
  "entrypoints": {
    "win-x64": { "executable": "runtimes/win-x64/SpotifyController.exe" },
    "osx-arm64": { "executable": "runtimes/osx-arm64/SpotifyController" },
    "linux-x64": { "executable": "runtimes/linux-x64/SpotifyController" }
  },
  "publisher": { "name": "Example Publisher" },
  "license": "MIT",
  "repository": "https://github.com/example/spotify-controller"
}
```

- Optional fields you did not supply are left out entirely, never written as empty strings. `license` is
  the exception: `MIT` is a deliberate default.
- `publisher` carries only `name`; set its `id`, `email` and `url` by hand.
- `version` always starts at `1.0.0`.
- Entrypoints use the `runtimes/<rid>/` layout, which keeps identically named macOS and Linux executables
  apart in one multi-platform package.

### macrodeck-build.json

The build recipe `build` reads. The manifest describes what a plugin is; this file says how to build it.

```json
{
  "version": 1,
  "targets": {
    "win-x64": {
      "executable": "dotnet",
      "arguments": ["publish", "SpotifyController.csproj", "-c", "Release",
                    "-r", "win-x64", "--self-contained", "true",
                    "-o", "bin/publish/win-x64"],
      "output": "bin/publish/win-x64"
    }
  }
}
```

- `executable` and `arguments` are separate values and never go through a shell.
- An optional `workingDirectory`, relative to the project root, runs the tool from somewhere else.
- `output` and `workingDirectory` must stay inside the project directory.
- Nothing here is .NET-specific: any toolchain can be described the same way.
- Generated targets publish **self-contained** Release builds. An entrypoint with no `runtime` block must
  not be a `.dll`.

## Exit codes

| Code | When |
| --- | --- |
| 0 | The project was created. |
| 1 | The template itself failed to generate (`template-create-failed`). |
| 2 | Invalid input, a missing required value, or a non-empty `--output` directory (`output-exists`). |
| 3 | `dotnet` is missing (`dotnet-not-found`) or the template could not be installed (`template-install-failed`). |
| 4 | You declined the confirmation. |
| 70 | Writing or rewriting the generated files failed. |

## See also

- [`build`](/cli/build/) - build and package the scaffolded project.
- [`run`](/cli/run/) - run it against a stub host.
- [Manifest reference](/reference/manifest/).
