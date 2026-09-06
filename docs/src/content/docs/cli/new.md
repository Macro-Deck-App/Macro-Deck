---
title: macrodeck-plugin new
description: Scaffold a new plugin project from the official template, interactively or from a script.
---

Scaffolds a new plugin project from the official
[plugin template](https://github.com/Macro-Deck-App/Macro-Deck-Plugin-Template), installing that template
first if it is not already there. Run with no options it walks an interactive wizard; given enough options
it runs without prompting at all, which is what makes it usable from a script or an IDE integration.

```bash
macrodeck-plugin new
```

| Option | Default | What it does |
| --- | --- | --- |
| `--name <name>` | prompted | The plugin's display name. Required. |
| `--id <id>` | prompted | The reverse-domain plugin id, validated by the same rule the manifest uses. Required, though `--yes` takes the id derived from `--name`. |
| `--publisher <name>` | prompted | The publisher's display name. Required. |
| `--description <text>` | `A Macro Deck plugin.` | Written to `manifest.description`. |
| `--repository <url>` | omitted | Absolute `http`/`https` URL. Omitted from the manifest when not given. |
| `--homepage <url>` | omitted | Absolute `http`/`https` URL. The wizard offers the repository URL as the prompt default; omitted from the manifest when not given. |
| `--license <spdx>` | `MIT` | Carried into `manifest.license` verbatim. |
| `--platform <rid>` | `win-x64`, `osx-arm64`, `linux-x64` | A target runtime identifier. Repeat for each platform. |
| `--project-name <name>` | derived from `--name` | The C# project and assembly name. |
| `--output <dir>` | `./<project name>` | Directory to scaffold into. |
| `-y`, `--yes` | off | Never prompt; accept every value the wizard would have offered as a default. |
| `--non-interactive` | off | Never prompt; a required value that was not supplied is a usage error rather than being defaulted. |
| `--template-version <v>` | latest prerelease | Install this exact template version instead. |
| `--skip-template-install` | off | Never probe or install the template; it must already be present. |
| `--no-restore` | off | Skip the NuGet restore `dotnet new` would otherwise run. |

`--platform` accepts `win-x64`, `win-arm64`, `osx-arm64`, `osx-x64`, `linux-x64` and `linux-arm64`.
Anything else is a usage error, and at least one platform must be selected. The wizard's own platform
prompt offers only `win-x64`, `osx-arm64` and `linux-x64` - the RIDs Macro Deck currently ships builds
for - while `--platform` still accepts all six. It renders as a checkbox list, or as a numbered list on a
terminal without ANSI support.

**Interactive or not.** The wizard runs only when stdin is a terminal and neither `--yes` nor
`--non-interactive` was given. Options you already passed become the offered defaults, so the wizard fills
gaps rather than re-asking everything. Without a terminal, a missing required value is a
`missing-required-option` usage error instead of a prompt that would hang a CI job.

The two suppressing flags differ in what they do about a value you did not pass. `--yes` accepts whatever
the wizard would have offered - so `--name "Acme Light Control" --publisher Acme --yes` is enough, and the
plugin id, project name and output directory are all derived. `--non-interactive` defaults nothing it
would otherwise have prompted for, so the same command without `--id` fails; use it when a script should
break rather than silently scaffold a plugin under a guessed id.

**Template handling.** `new` first probes whether the template is installed, and only reaches the network
when it is not - so a second run is offline and fast. When it does install, it uses
`MacroDeck.Plugin.Templates@*-*`; the `*-*` matters because only `-preview` versions are published ahead
of the 3.0 release. An install failure surfaces the underlying `dotnet` output as the diagnostic's detail.

**What gets written.** Optional manifest fields are omitted entirely when you did not supply a value -
never written as an empty string - with `license` the deliberate exception, since `MIT` is an intentional
default. `publisher` carries only `name`; its `id`, `email` and `url` are not part of this command.
The initial `version` is always `1.0.0`.

Only the platforms you selected are emitted, into both `manifest.entrypoints` and the generated
`macrodeck-build.json`, so a scaffolded plugin never claims support for a platform you did not ask for.
Entrypoints use the RID-specific layout publication expects:

```json
"entrypoints": {
  "win-x64": { "executable": "runtimes/win-x64/SpotifyController.exe" },
  "osx-arm64": { "executable": "runtimes/osx-arm64/SpotifyController" },
  "linux-x64": { "executable": "runtimes/linux-x64/SpotifyController" }
}
```

The `runtimes/<rid>/` prefix is what keeps a macOS and a Linux build - which produce identically named
executables - from colliding inside one multi-platform package.

**`macrodeck-build.json`** is written next to `manifest.json` and holds the build recipe [`build`](/cli/build/)
reads, which the runtime manifest deliberately does not: a manifest describes what a plugin *is*, never how
to build it. Each target names its tool and arguments as separate values rather than one shell string, so
nothing is passed through a shell:

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

A target may also carry an optional `workingDirectory`, relative to the project root, for a toolchain that
must run from somewhere other than the directory holding the manifest. Every path in the file - `output` and
`workingDirectory` alike - must stay inside the project directory.

Nothing about that shape is .NET-specific - a plugin built with another toolchain writes its own
`executable`/`arguments`/`output` and is equally buildable. Generated projects publish **self-contained**:
an entrypoint with no `runtime` block must not be a `.dll`, and self-contained output is also what makes
the per-RID layout meaningful.

Exit code is `Success` (0), `UsageError` (2) for invalid input or a non-empty output directory,
`InputUnreadable` (3) when `dotnet` is missing or the template could not be installed, `SubjectInvalid` (1)
when the template itself failed to generate, and `Cancelled` (4) if you decline the confirmation.
