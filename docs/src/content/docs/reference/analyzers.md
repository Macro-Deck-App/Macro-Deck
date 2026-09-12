---
title: Analyzers
description: Roslyn diagnostics provided by MacroDeck.Plugin.Analyzers.
---

`MacroDeck.Plugin.Analyzers` catches common plugin mistakes at build time.

```xml
<PackageReference Include="MacroDeck.Plugin.Analyzers" Version="3.0.0" PrivateAssets="all" />
```

Keep `PrivateAssets="all"`; analyzers are build-time tooling and should not become a runtime dependency of your plugin.

## Diagnostics

Diagnostic ids are stable public tooling contracts.

| Id | Severity | Meaning |
| --- | --- | --- |
| <a id="mdp1001"></a>`MDP1001` | Error | `manifest.json` has missing or invalid plugin identity fields. |
| <a id="mdp1002"></a>`MDP1002` | Error | A statically known declared capability/action local id is invalid. |
| <a id="mdp1003"></a>`MDP1003` | Error | The manifest icon uses an unsupported file extension. |
| <a id="mdp1004"></a>`MDP1004` | Error | An `IPluginIntegration` type restates identity/icon metadata owned by the manifest. |
| <a id="mdp2001"></a>`MDP2001` | Error | A statically visible capability id is declared more than once for the same kind. |
| <a id="mdp2002"></a>`MDP2002` | Error | A capability handler declares an unknown capability kind. |
| <a id="mdp2003"></a>`MDP2003` | Warning | A capability handler is registered as a service but not through the capability registration path. |
| <a id="mdp2004"></a>`MDP2004` | Warning | An `IPluginIntegration` is registered directly instead of through `RegisterIntegration`. |
| <a id="mdp2005"></a>`MDP2005` | Error | Plugin code maps a route below the reserved `/_macrodeck` prefix. |
| <a id="mdp2006"></a>`MDP2006` | Warning | `[MacroDeckIntegration]` is applied to an out-of-process `IPluginIntegration` where it has no effect. |
| <a id="mdp3001"></a>`MDP3001` | Warning | Async capability/action code fails to forward the cancellation token already in scope. |
| <a id="mdp3002"></a>`MDP3002` | Warning | Blocking waits or sleeps are used inside SDK execution contracts. |
| <a id="mdp3003"></a>`MDP3003` | Warning | An SDK contract implementation exposes `async void`. |
| <a id="mdp4001"></a>`MDP4001` | Error | Singleton registration captures invocation-scoped context. |
| <a id="mdp4002"></a>`MDP4002` | Warning | A plugin overrides the listener URL managed by Macro Deck hosting. |
| <a id="mdp5001"></a>`MDP5001` | Warning | Code uses an `[Obsolete]` Macro Deck SDK/hosting/protocol API. |
| <a id="mdp5002"></a>`MDP5002` | Warning | Code uses an API carrying Macro Deck deprecation metadata. |
| <a id="mdp5003"></a>`MDP5003` | Warning | Macro Deck deprecation metadata is internally inconsistent or lacks required guidance. |
| <a id="mdp5004"></a>`MDP5004` | Error | Code uses an API whose declared removal version has already been reached. |
| <a id="mdloc001"></a>`MDLOC001` | Error | A localization key exists in a translation but has no default-language resource. |
| <a id="mdloc002"></a>`MDLOC002` | Error | A translation's placeholders differ from the default language's. |
| <a id="mdloc003"></a>`MDLOC003` | Error | The same localization key is declared twice in one resource file. |
| <a id="mdloc004"></a>`MDLOC004` | Error | A localization parameter declaration in a resource comment is not usable. |
| <a id="mdloc005"></a>`MDLOC005` | Error | A resource file's culture suffix is not a well-formed culture name. |
| <a id="mdloc006"></a>`MDLOC006` | Error | Code references a Macro Deck catalog key that has been removed. |
| <a id="mdloc007"></a>`MDLOC007` | Error | A plural family has a form name outside the closed set, or no `Other` form to fall back on. |
| <a id="mdloc008"></a>`MDLOC008` | Error | A key is both a member and the group other keys nest under. |

The analyzer intentionally reports only cases it can establish from syntax/symbols or declared additional files. Values assembled dynamically at runtime remain runtime validation responsibilities.

Two consequences worth knowing. `MDP5002` now fires on real APIs rather than none: the deprecated widget state members are the first entries in the deprecation registry, so a plugin that restyles a widget by the old `WidgetStateSelector` reports at every call site - see [migrations](/policies/migrations/). And the state ids an action returns from `IStateProviderActionDefinition` are **not** covered by `MDP1002`, because they come back from a runtime call rather than a constant the analyzer can read; their grammar is enforced by the host when a button adopts them, and by a conformance check. There is deliberately no analyzer rule for them - one would be dead code.

## Manifest and launch settings

NuGet imports the analyzer package's build props so `manifest.json` and `Properties/launchSettings.json` can be inspected as additional files when present. This allows identity/icon/listener diagnostics to run without turning those files into source inputs.

If you consume the analyzer project directly from source instead of through NuGet, ensure the required files are added as `AdditionalFiles` in the test/fixture project.

## Suppression

Use normal Roslyn mechanisms and keep suppressions narrow. A suppression should explain why the general rule does not apply in that location.

```csharp
#pragma warning disable MDP3002 // Third-party API has no asynchronous equivalent.
var result = LegacySdk.CallSync();
#pragma warning restore MDP3002
```

Project-wide suppression:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);MDP2003</NoWarn>
</PropertyGroup>
```

Escalate a warning when your project wants a stronger policy:

```xml
<PropertyGroup>
  <WarningsAsErrors>$(WarningsAsErrors);MDP5001</WarningsAsErrors>
</PropertyGroup>
```

Do not suppress identity, compatibility, or lifecycle diagnostics merely to make the build pass when the runtime would reject or mis-handle the same configuration.

## Runtime validation still applies

Analyzers supplement runtime/package validation; they do not replace it. Dynamic ids, provider data, actual manifest contents in a packaged artifact, DI graphs, protocol compatibility, and package structure are still validated by the hosting/packaging runtime.

For package/manifest checks use `macrodeck-plugin validate` and the packaging workflow described in the [plugin CLI](/cli/) section.

## Compatibility diagnostics

`MDP5001`-`MDP5004` help plugin authors migrate before an SDK removal becomes a hard incompatibility. Macro Deck deprecation metadata records the deprecation/removal lifecycle; see [Deprecations](/policies/deprecations/).

## Localization diagnostics

`MDLOC001`-`MDLOC008` are reported by the same source generator that turns `Localization/*.resx` into a typed API - there is no separate analyzer to enable. `MDLOC006` is this family's compatibility diagnostic: it plays the same role for a retired Macro Deck catalog key that `MDP5004` plays for a removed SDK API. See [Localization](/features/localization/) for how to add resources, what each diagnostic means in practice, and the recommended editing workflow.

## Related documentation

- [SDK reference](/reference/sdk-packages/)
- [Plugin hosting](/reference/plugin-hosting/)
- [Plugin CLI](/cli/)
- [Deprecations](/policies/deprecations/)
- [Localization](/features/localization/)
