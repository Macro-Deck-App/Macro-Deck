---
title: Deprecations
description: The lifecycle for deprecating and removing a Macro Deck SDK API, how the host reports confirmed usage, and the compatibility states shown to a plugin's users.
---

Macro Deck tells you an SDK API is going away twice: once at compile time, as an analyzer warning with
the removal version and the replacement in the message, and once at run time, in the desktop app, where
the person running your plugin can see it.

This page is the contract behind both. It is also machine-checked - `SdkDeprecationLifecycleTests` reads
the registry table below, so an API cannot be removed from the SDK without its lifecycle appearing here.

## The lifecycle

1. **Deprecate.** The API gets a standard `[Obsolete]` *and* a `[MacroDeckDeprecated]` carrying the
   version it was deprecated in, the version it will be removed in, and what to do instead. It keeps
   working exactly as before. An entry is added to `SdkDeprecations.Active` and to the table below.
2. **Warn.** Plugin builds report [MDP5002](/sdk/analyzers/#mdp5002) at every call site. The host reports
   the same finding, under the same id, to the user running the plugin.
3. **Remove.** At the declared removal version - never earlier - the member is deleted, and its registry
   entry moves from `Active` to `Removed`. It stays in `Removed`, and in the table below, forever: a
   plugin built years ago still reports that API, and "removed in 4.0.0, use X" is a better answer than
   "unknown".

Removal only ever happens in a major release, and only for an API whose `RemovedIn` names that release.
An API still present after its declared removal version is itself a bug, reported as
[MDP5004](/sdk/analyzers/#mdp5004).

## Declaring a deprecation

```csharp
[Obsolete("Use IIntegrationContext.SetVariable(string, VariableValue). Removed in Macro Deck 4.0.0.")]
[MacroDeckDeprecated("3.1.0", "4.0.0", "Pass a typed VariableValue instead of a boxed object.",
	Replacement = "MacroDeck.Sdk.IIntegrationContext.SetVariable(string, VariableValue)",
	MigrationUrl = "https://docs.macro-deck.app/policies/deprecations/")]
public void SetVariable(string name, object value) { }
```

Both attributes, always. `[Obsolete]` is what the compiler, the IDE and every third-party analyzer
already understand; `[MacroDeckDeprecated]` carries the lifecycle that `[Obsolete]` has nowhere to put.
Declaring one without the other, or a removal version that is not after the deprecation version, or empty
guidance, is reported as [MDP5003](/sdk/analyzers/#mdp5003).

The attribute is public, so a plugin may use it on its own surface too - the analyzer rules apply to any
assembly that declares it.

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
fixed four-way `WidgetStateSelector` and the flag that only ever answered "does this widget have two
appearances" are both superseded by state ids: `WidgetAppearanceRequest.StateIds` and
`WidgetTargetInfo.States`.

## How the host knows what your plugin uses

The interesting problem is telling "this plugin *calls* a deprecated API" apart from "this plugin was
*built against* an SDK in which something is deprecated". Only the first is worth showing a user as a
fact. The host therefore labels every finding with the evidence behind it:

| Source | Means |
| --- | --- |
| `confirmed` | Your plugin reported this exact API in its build-time usage manifest. |
| `negotiated` | Observed directly during protocol or capability negotiation. |
| `inferred` | Derived from your SDK version alone - the plugin *may* be affected. Never presented as fact. A plugin that does not reference the analyzer package lands here. |
| `unknown` | The plugin reported nothing the host could reason from. |

The usage manifest is what makes `confirmed` possible. `MacroDeck.Plugin.Analyzers` includes a source
generator that records the deprecated APIs your compilation actually references and emits them as an
assembly attribute, which `MacroDeck.Plugin.Hosting` reads at startup and sends in the session handshake.
It is derived from the same semantic analysis that raises MDP5002, so the manifest and the warnings
cannot disagree.

An empty list and a missing list mean different things and are never conflated: empty is "the generator
ran, and this plugin uses none", which is a positive statement; missing is "no manifest", which is only
ever enough for an inference.

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

## Diagnostic ids

Compile-time ids are documented in the [Analyzers reference](/sdk/analyzers/). The host reuses `MDP5002` and `MDP5004`
for the same findings, so a warning you saw while building and a row your user sees are visibly the same
thing. Three ids exist only at run time, because nothing about them is visible at compile time:

| Id | Meaning |
| --- | --- |
| `MDP5005` | The plugin *may* use a deprecated API, inferred from its SDK version. |
| `MDP5006` | A declared capability the host did not accept. |
| `MDP5007` | No protocol version in common. |

These are reserved in the same `MacroDeck.Compatibility` band as the analyzer rules and must not be
reused for a future analyzer rule.
