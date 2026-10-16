---
title: Agent skills
description: Official skills that let coding agents such as Claude Code, Codex, OpenCode and GitHub Copilot create, change, test and prepare Macro Deck plugins for the Store.
---

If you build plugins with a coding agent, install the official Macro Deck skills from
[Macro-Deck-Skills](https://github.com/Macro-Deck-App/Macro-Deck-Skills). A skill gives the
agent the Macro Deck workflow and points it at the official sources, so it does not have to guess SDK
APIs, manifest fields or Store rules.

The skills use the portable `SKILL.md` format and work with agent hosts that support Agent Skills,
including Claude Code, Codex, OpenCode and GitHub Copilot.

## macro-deck-plugin-creator

Takes a rough plugin idea to a tested, licensed and release-ready plugin repository:

- researches and plans the plugin, with independent reviews of the plan and the result;
- implements it on the current SDK and [plugin template](/introduction/samples-and-template/);
- tests it with the [plugin CLI](/cli/) and the [conformance suite](/creator-portal/conformance/);
- writes the documentation and license files and prepares the
  [release for the Store](/creator-portal/release-workflow/).

It also changes an existing Macro Deck 3 plugin in place, checks a plugin for Store readiness, and
recognizes Macro Deck 2 projects instead of treating them as Macro Deck 3 plugins.

Every run looks up the volatile facts (SDK and CLI versions, manifest schema, Creator Guidelines,
dependency policy) from the official sources, so an installed skill does not go stale when the SDK
moves on.

## Install

With the [GitHub CLI](https://cli.github.com/):

```bash
gh skill install Macro-Deck-App/Macro-Deck-Skills macro-deck-plugin-creator
```

`gh skill` installs into the location your agent expects; pass `--agent` to pick a specific one.
Update later with:

```bash
gh skill update macro-deck-plugin-creator
```

The [repository README](https://github.com/Macro-Deck-App/Macro-Deck-Skills#readme) covers the
required GitHub CLI version, manual installation without `gh skill`, and pinning a skill version.

Building plugins with the skill needs the same tools as building them by hand: the
[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and Git. Its optional helper scripts
need Python 3.

## Use it

Restart or reload the agent after installing, then describe the plugin you want, for example:

```text
Create a Macro Deck plugin that toggles my Philips Hue lights and shows each light's state on its button.
```

Most agents pick the skill up from a request like that; you can also invoke it by name
(`/macro-deck-plugin-creator` in Claude Code).

The skill does not replace your review. Read what the agent produced, run the plugin against a real
Macro Deck before you publish it, and [publish](/guides/publishing/) only code you understand.
