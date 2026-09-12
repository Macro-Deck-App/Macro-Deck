---
title: Deprecations
description: The lifecycle for deprecating and removing a Macro Deck SDK API, how the host reports confirmed usage, and the compatibility states shown to a plugin's users.
---

A deprecated Macro Deck SDK API carries two attributes. This is a real one, from
`MacroDeck.Sdk.Widgets.WidgetTargetInfo`:

```csharp
[Obsolete("Use States.Count > 1. Removed in Macro Deck 4.0.0.")]
[MacroDeckDeprecated("3.0.0",
	"4.0.0",
	"Read States.Count > 1 instead of this collapsed on/off flag.",
	Replacement = "MacroDeck.Sdk.Widgets.WidgetTargetInfo.States")]
public bool HasOnOffStates { get; init; }
```

You are told twice: at compile time, as an analyzer warning with the removal version and the
replacement in the message, and at run time, in the desktop app, where the person running your plugin
sees the same finding under the same id.

```csharp
bool toggle = info.HasOnOffStates;       // MDP5002 (and CS0618)
bool toggle = info.States.Count > 1;     // fixed
```

This page is the contract behind both, and it is machine-checked: `SdkDeprecationLifecycleTests` checks
every registry entry against [the registry](#the-registry) below, so an API cannot be removed from the
SDK without its lifecycle appearing here.

## The attribute

`MacroDeckDeprecatedAttribute` lives in `MacroDeck.Sdk.Deprecation`.

| Argument | Kind | Meaning |
| --- | --- | --- |
| `deprecatedIn` | constructor, required | The `major.minor.patch` the API was deprecated in. |
| `removedIn` | constructor, required | The release that removes it. Must be after `deprecatedIn`. |
| `guidance` | constructor, required | What to do instead, in one sentence. Must not be empty. |
| `Replacement` | named, optional | The replacement API as a searchable display name; omit when there is no one-for-one replacement. |
| `MigrationUrl` | named, optional | A deep link to longer migration notes. |

Both attributes, always. `[Obsolete]` is what the compiler, the IDE and every third-party analyzer
already understand; `[MacroDeckDeprecated]` carries the lifecycle `[Obsolete]` has nowhere to put.

The attribute is public, so a plugin may use it on its own surface too - the analyzer rules apply to any
assembly that declares it.

## What you see

| Id | Severity | When |
| --- | --- | --- |
| [MDP5001](/reference/analyzers/#mdp5001) | Warning | You use an `[Obsolete]` SDK, hosting or protocol API that carries no deprecation metadata. |
| [MDP5002](/reference/analyzers/#mdp5002) | Warning | You use an API carrying `[MacroDeckDeprecated]`; the message names the API, both versions and the guidance. |
| [MDP5003](/reference/analyzers/#mdp5003) | Warning | A declaration is missing its companion `[Obsolete]`, has a removal version not after the deprecation version, or has empty guidance. |
| [MDP5004](/reference/analyzers/#mdp5004) | Error | You use an API whose declared removal version has been reached. An API still present after that version is itself a bug. |

## The lifecycle

| Stage | What happens | When |
| --- | --- | --- |
| 1. Deprecate | The API gets `[Obsolete]` *and* `[MacroDeckDeprecated]`. It keeps working exactly as before. An entry is added to `SdkDeprecations.Active` and to the table below. | Any release |
| 2. Warn | Plugin builds report MDP5002 at every call site. The host reports the same finding, under the same id, to the user running the plugin. | At least one release cycle before removal |
| 3. Remove | The member is deleted and its registry entry moves from `Active` to `Removed`. | Only in a major release, and only the one its `RemovedIn` names - never earlier |
| 4. Remember | The entry stays in `Removed`, and in the table below, forever: a plugin built years ago still gets "removed in 4.0.0, use X" rather than "unknown". | Permanently |

To migrate, follow the replacement the warning names; longer write-ups are in
[migrations](/policies/migrations/).

## The registry

Deprecated, still present:

| API | Deprecated in | Removal planned | Replacement |
| --- | --- | --- | --- |
| `MacroDeck.Sdk.Widgets.WidgetStateSelector` | 3.0.0 | 4.0.0 | `MacroDeck.Sdk.Widgets.WidgetAppearanceRequest.StateIds` |
| `MacroDeck.Sdk.Widgets.WidgetAppearanceRequest.State` | 3.0.0 | 4.0.0 | `MacroDeck.Sdk.Widgets.WidgetAppearanceRequest.StateIds` |
| `MacroDeck.Sdk.Widgets.WidgetTargetInfo.HasOnOffStates` | 3.0.0 | 4.0.0 | `MacroDeck.Sdk.Widgets.WidgetTargetInfo.States` |

Removed:

| API | Deprecated in | Removed in | Replacement |
| --- | --- | --- | --- |
| _None yet._ | | | |

Action buttons moved from a binary on/off appearance to N states with stable ids (issue #612), so the
fixed four-way `WidgetStateSelector` and the two-appearance flag are superseded by
`WidgetAppearanceRequest.StateIds` and `WidgetTargetInfo.States`.

### How the registry is enforced

The registry is `SdkDeprecations` in `MacroDeck.Sdk.Deprecation`, keyed by documentation comment id
(`T:`, `M:`, `P:`, `E:` or `F:`). `SdkDeprecationLifecycleTests` fails the build when:

- a member carries `[MacroDeckDeprecated]` without a registry entry, or without `[Obsolete]`;
- an entry's removal version is not after its deprecation version, or its guidance is empty;
- an `Active` entry no longer exists in the SDK, or a `Removed` entry still does;
- an entry has no row on this page.

## How the host knows what your plugin uses

"This plugin *calls* a deprecated API" and "this plugin was *built against* an SDK in which something is
deprecated" are different, and only the first is shown to a user as fact. Every finding carries its
evidence:

| Source | Means |
| --- | --- |
| `confirmed` | Your plugin reported this exact API in its build-time usage manifest. |
| `negotiated` | Observed directly during protocol or capability negotiation. |
| `inferred` | Derived from your SDK version alone - the plugin *may* be affected. Never presented as fact. A plugin that does not reference the analyzer package lands here. |
| `unknown` | The plugin reported nothing the host could reason from. |

`MacroDeck.Plugin.Analyzers` includes a source generator that records the deprecated APIs your
compilation actually references and emits them as an assembly attribute; `MacroDeck.Plugin.Hosting`
reads it at startup and sends it in the session handshake. It uses the same semantic analysis that
raises MDP5002, so the manifest and the warnings cannot disagree.

An empty list and a missing list are never conflated: empty means "the generator ran, and this plugin
uses none"; missing means "no manifest", which is only ever enough for an inference.

Nothing in the manifest is a secret - it is a list of Macro Deck's own API names, capped at 64 entries.

## Compatibility states

The desktop app shows one state per plugin, in the Developer page's Compatibility tab and as a badge on
the plugin's card. A plugin's state is the *worst* of everything found about it, in this order:

| State | Means |
| --- | --- |
| `compatible` | Nothing to report. |
| `deprecated_apis` | Confirmed use of deprecated APIs, none due for removal yet. |
| `update_recommended` | Nothing is broken, but the plugin is behind - typically an older SDK. |
| `update_required` | Confirmed use of an API whose declared removal version has been reached. |
| `partially_incompatible` | The session works, but at least one declared capability was rejected. |
| `incompatible` | No negotiable protocol version - the plugin cannot connect at all. |

## Run-time diagnostic ids

The host reuses `MDP5002` and `MDP5004` for the same findings, so a warning you saw while building and a
row your user sees are visibly the same thing. Three ids exist only at run time, because nothing about
them is visible at compile time:

| Id | Meaning |
| --- | --- |
| `MDP5005` | The plugin *may* use a deprecated API, inferred from its SDK version. |
| `MDP5006` | A declared capability the host did not accept. |
| `MDP5007` | No protocol version in common. |

These are reserved in the same `MacroDeck.Compatibility` band as the analyzer rules and must not be
reused for a future analyzer rule.

## See also

- [Compatibility policy](/policies/compatibility/) - what is frozen and what counts as a break.
- [Migrations](/policies/migrations/) - migration guides for superseded APIs.
- [Analyzers](/reference/analyzers/) - MDP5001-MDP5004 in full.
- [ADR 0037](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0037-sdk-deprecation-is-declared-metadata.md) - why deprecation is declared metadata.
