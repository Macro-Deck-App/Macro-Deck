---
title: Samples and template
description: The two companion repositories - a plugin template to start from, and worked sample plugins to read - and when to reach for which.
---

Two repositories sit alongside the main Macro Deck repository. Neither is part of the host: they exist
so a plugin author has both a starting point and a worked reference.

| Repository | Use it when |
| --- | --- |
| [Macro-Deck-Plugin-Template](https://github.com/Macro-Deck-App/Macro-Deck-Plugin-Template) | You are starting a new plugin and want the project layout, `manifest.json` and `Program.cs` already in place. |
| [Macro-Deck-Sample-Plugins](https://github.com/Macro-Deck-App/Macro-Deck-Sample-Plugins) | You have a plugin and want to see how a capability is actually implemented against a real host. |

## The template

The template repository is the intended way to start a plugin, and
[`macrodeck-plugin new`](/cli/new/) is the intended way to reach it - it installs the
template for you, collects the metadata, and writes only the platforms you actually target:

```bash
macrodeck-plugin new
```

Compared with invoking the template directly, `new` also fills in the publisher, license, repository and
homepage, and generates the `macrodeck-build.json` build recipe alongside the manifest, which [`macrodeck-plugin build`](/cli/build/) then consumes to build and package every declared platform.

The template is still usable on its own if you would rather not install the CLI:

```bash
dotnet new install "MacroDeck.Plugin.Templates@*-*"
```

```bash
dotnet new macrodeck-plugin \
  -n Acme.LightControl \
  -o Acme.LightControl \
  --pluginId com.acme.light-control \
  --pluginName "Acme Light Control"
```

`@*-*` selects the newest published template while Macro Deck 3 packages are prerelease builds. The
standard `-o` option creates a new directory; omit it only when the current directory is already the
project directory.

The generated repository targets `net10.0`, includes the ASP.NET Core framework reference, copies
`manifest.json` and its icon to the output, and starts through
`MacroDeckPlugin.CreatePlugin(args)`. Follow the [quickstart](/introduction/quickstart/) to run it.

If you prefer to understand and create each file yourself, use
[Create a plugin manually](/introduction/manual-setup/). Cloning the template repository and
renaming its project, namespace and manifest fields by hand is also supported; the repository README
tracks the complete rename checklist.

## The sample plugins

The sample plugins are worked, out-of-process plugins exercising actions, variables, events, config
flow, weather and icons together against a real host. They live in their own repository because they
are published examples rather than part of the host's build, and they are updated there.

Read a sample when the reference tells you *what* a contract is and you want to see *how* it is
satisfied end to end - what a config flow's steps look like in practice, how a provider-shaped
capability declares one provider and addresses runtime instances, or how `IPluginCatalogNotifier` is
used to tell the host a catalogue changed.

## What ships inside this repository instead

Two things a plugin author might expect to find in the samples repository deliberately do not:

- **`MacroDeck.Plugin.Testing`** - the loopback test host, fakes and assertions used to test a plugin
  without a real Macro Deck. See [testing plugins](/sdk/testing/).
- **The conformance suite** - the framework-agnostic contract suite, runnable with
  `macrodeck-plugin test`, with stable check ids you can gate CI on. See
  [conformance](/sdk/conformance/).

Both are shipped packages, not samples: use them in your own test project rather than copying anything
out of a sample.

## See also

- [Quickstart](/introduction/quickstart/) - create and run a plugin from the template.
- [Create a plugin manually](/introduction/manual-setup/) - assemble the same required files
  yourself.
- [Debugging plugins](/guides/debugging/) - run the project from an IDE against a real host.
- [Plugin hosting](/sdk/hosting/) - the builder API and registration modes the template uses.
- [Contributing an integration](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/development/contributing-integrations.md) - if what you want is an *in-process*
  integration shipped with the host, rather than a plugin of your own.
