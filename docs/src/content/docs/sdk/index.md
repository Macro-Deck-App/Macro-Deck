---
title: SDK overview
description: The packages a Macro Deck plugin builds against, the integration model they share, and where each area is documented.
---

The SDK is split into small packages so a plugin depends only on the contracts and tooling it needs. Every public member is documented through package XML documentation and IntelliSense; this section describes what each package is for, the model they share, and where each area is covered in depth.

Everything here is a compatibility commitment: see the [compatibility policy](/policies/compatibility/) for what may change and how.

## Packages

| Package | Use it for |
| --- | --- |
| `MacroDeck.Sdk` | Integration lifecycle, actions, capabilities, variables, events, profiles, decks, widgets, notifications, configuration flows, and UI session providers. |
| `MacroDeck.Plugin.Protocol` | Low-level plugin wire contracts. Most plugins use `MacroDeck.Plugin.Hosting` instead. |
| `MacroDeck.Plugin.Packaging` | Manifest and `.macroDeckPlugin` package model. |
| `MacroDeck.Plugin.Hosting` | Hosting an out-of-process plugin with ASP.NET Core and the Macro Deck protocol. |
| `MacroDeck.Plugin.Serilog` | Forwarding plugin Serilog events to the Macro Deck host. |
| `MacroDeck.Plugin.Testing` | Plugin test host, fakes, and conformance support. |
| `MacroDeck.Plugin.Analyzers` | Compile-time diagnostics for common plugin mistakes and compatibility problems. |
| `MacroDeck.Plugin.Cli` | `macrodeck-plugin` developer tooling. |
| `MacroDeck.Localization` | Localization keys, deferred `LocalizedString`/`LocalizedText` values, the culture fallback chain, and the reusable `MacroDeckStrings` catalog. Paired with `MacroDeck.Plugin.Analyzers`, whose source generator turns a plugin's `Localization/*.resx` into a typed API. See [Localization](/sdk/localization/). |
| `MacroDeck.Ui.Model` | Transport-neutral UI tree, events, patches, resources, and capability negotiation - the wire contract underneath the Macro Deck UI framework. |
| `MacroDeck.Ui` | The Macro Deck UI framework: a declarative C# DSL and reactive runtime over `MacroDeck.Ui.Model`. |
| `MacroDeck.Ui.Testing` | Headless testing for Macro Deck UI views. |

Package versions follow the Macro Deck release they were built with. Package-local README files provide the NuGet overview for each package.

## Core integration model

An integration exposes actions through the SDK and opts into additional functionality by implementing capability interfaces. Out-of-process plugins use the same capability contracts through `MacroDeck.Plugin.Hosting`.

Important areas include:

- Actions: `MacroDeck.Sdk.Actions` - including `IStateProviderActionDefinition`, which lets a configured action instance supply an Action Button's states and its current state
- Configuration flows: `MacroDeck.Sdk.ConfigFlow`
- Variables: `MacroDeck.Sdk.Variables`
- Events: `MacroDeck.Sdk.Events`
- Profiles and deck navigation: `MacroDeck.Sdk.Profiles` and `MacroDeck.Sdk.Decks`
- Widgets: `MacroDeck.Sdk.Widgets` - a widget's states are addressed by stable id; the older `WidgetStateSelector` is deprecated, see [migrations](/policies/migrations/)
- Music and weather providers: `MacroDeck.Sdk.MusicPlayer` and `MacroDeck.Sdk.Weather`
- Integration issues and logging: `MacroDeck.Sdk.Issues` and `MacroDeck.Sdk.Logging`
- UI session providers: `MacroDeck.Sdk.Ui` - `IUiProvider` and `IUiSession`, expressed in `MacroDeck.Ui.Model` types so a provider can serve a tree without taking the UI framework as a dependency. See [Macro Deck UI](/sdk/ui/).

Use IntelliSense for individual member contracts instead of treating this site as a manually maintained copy of every public type.

## Logging

Use the SDK logging abstractions for integration diagnostics and `MacroDeck.Plugin.Serilog` when an out-of-process plugin should forward Serilog events into the host log pipeline. Do not log credentials or other reusable secrets.

## Conditional fields

Configuration inputs can be conditionally visible while remaining part of the form state. This differs from structurally omitting an element from a Macro Deck UI tree. See [Config and action flows](/sdk/flows/) and [State and bindings](/sdk/ui/concepts/state-and-bindings/) for the two models.

## Capability identity

Declared capability ids are local ids. Macro Deck qualifies them with the owning integration or plugin identity before registration. Do not construct qualified ids manually when an SDK API asks for a local id.

## In this section

| Page | What it covers |
| --- | --- |
| [Plugin hosting](/sdk/hosting/) | `MacroDeck.Plugin.Hosting`: the builder API, dependency injection, registration modes, lifecycle and the reserved routes. |
| [Capabilities](/sdk/capabilities/) | Every capability interface an integration can opt into, and how to choose between them. |
| [Config and action flows](/sdk/flows/) | The two unrelated things Macro Deck calls a flow, and what each one is for. |
| [Macro Deck UI](/sdk/ui/) | The declarative UI framework: authoring a view, state and bindings, the component catalog, serving it to a client, and testing it headlessly. |
| [Localization](/sdk/localization/) | `Localization/*.resx`, the generated typed API, the fallback chain and the `MDLOC` diagnostics. |
| [Logging and health](/sdk/logging/) | Forwarding Serilog into the host log viewer, and what the health endpoints answer. |
| [Authentication](/sdk/authentication/) | Credentials, session tokens, pairing, and what a plugin must never do with either. |
| [Testing plugins](/sdk/testing/) | `MacroDeck.Plugin.Testing`: the harness, the protocol host and what to assert. |
| [Conformance suite](/sdk/conformance/) | The fixed, framework-agnostic contract checks any plugin can be run against. |
| [Analyzers](/sdk/analyzers/) | The compile-time diagnostics `MacroDeck.Plugin.Analyzers` reports, and how to suppress one. |
| [Capability parity](/sdk/capability-parity/) | Where an out-of-process plugin behaves differently from an in-process integration. |

Beyond the SDK: the [plugin CLI](/cli/) builds, validates and packages what you write here, and the [reference](/reference/manifest/) section documents the manifest and the wire protocol underneath it.
