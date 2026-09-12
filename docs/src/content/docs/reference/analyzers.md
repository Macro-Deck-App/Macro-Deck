---
title: Analyzers
description: Roslyn diagnostics provided by MacroDeck.Plugin.Analyzers, each with a snippet that triggers it and the fix.
---

`MacroDeck.Plugin.Analyzers` reports plugin mistakes at build time. The plugin template already references it:

```xml
<PackageReference Include="MacroDeck.Plugin.Analyzers" PrivateAssets="all" />
```

Keep `PrivateAssets="all"`: it is build-time tooling and must not become a runtime dependency of your plugin.

## Example

A manifest id with an underscore and a `.bmp` icon, built with `dotnet build`:

```text
manifest.json(4,9): error MDP1001: manifest.json declares id 'Hello_Deck', which is not usable. It must be
  reverse-domain, lowercase and hyphen-separated, with at least two segments (e.g. 'com.example.my-plugin').
manifest.json(8,11): error MDP1003: manifest.json declares icon 'Assets/icon.bmp', whose extension has no
  known media type. Supported extensions are: .svg, .png, .jpg, .jpeg, .webp.
```

Every diagnostic carries a help link to its heading on this page. **Diagnostic ids are stable public contracts**: an id never changes meaning, and a rule that no longer applies is deprecated rather than reused.

## Diagnostics

| Id | Severity | Reported when |
| --- | --- | --- |
| [MDP1001](#mdp1001) | Error | `manifest.json` has a missing or invalid `id`, `name` or `version`. |
| [MDP1002](#mdp1002) | Error | A statically known action or declared capability local id is invalid. |
| [MDP1003](#mdp1003) | Error | The manifest icon has an unsupported file extension. |
| [MDP1004](#mdp1004) | Error | An `IPluginIntegration` type restates identity or icon metadata the manifest owns. |
| [MDP2001](#mdp2001) | Error | A statically visible capability id is declared twice for the same kind. |
| [MDP2002](#mdp2002) | Error | A capability handler declares an unknown kind. |
| [MDP2003](#mdp2003) | Warning | A capability handler is registered as a service but not as `ICapabilityHandler`. |
| [MDP2004](#mdp2004) | Warning | An `IPluginIntegration` is registered directly instead of through `RegisterIntegration`. |
| [MDP2005](#mdp2005) | Error | Plugin code maps a route below the reserved `/_macrodeck` prefix. |
| [MDP2006](#mdp2006) | Warning | `[MacroDeckIntegration]` is on an out-of-process `IPluginIntegration`, where it does nothing. |
| [MDP3001](#mdp3001) | Warning | Capability or action code discards the cancellation token in scope. |
| [MDP3002](#mdp3002) | Warning | A blocking wait or sleep runs inside an SDK contract member. |
| [MDP3003](#mdp3003) | Warning | An SDK contract type declares an `async void` member. |
| [MDP4001](#mdp4001) | Error | A singleton's constructor takes invocation-scoped context. |
| [MDP4002](#mdp4002) | Warning | The plugin overrides the listener URL Macro Deck assigns. |
| [MDP5001](#mdp5001) | Warning | Code uses an `[Obsolete]` Macro Deck SDK, hosting or protocol API. |
| [MDP5002](#mdp5002) | Warning | Code uses an API that carries Macro Deck deprecation metadata. |
| [MDP5003](#mdp5003) | Warning | Macro Deck deprecation metadata is inconsistent or has no guidance. |
| [MDP5004](#mdp5004) | Error | Code uses an API whose declared removal version has been reached. |
| [MDLOC001](#mdloc001) | Error | A key exists in a translation but not in the default-language resource. |
| [MDLOC002](#mdloc002) | Error | A translation's placeholders differ from the default language's. |
| [MDLOC003](#mdloc003) | Error | A key is declared twice in one resource file. |
| [MDLOC004](#mdloc004) | Error | A placeholder type declaration in a resource comment is not usable. |
| [MDLOC005](#mdloc005) | Error | A resource file's culture suffix is not a well-formed culture name. |
| [MDLOC006](#mdloc006) | Error | Code references a Macro Deck catalog key that has been removed. |
| [MDLOC007](#mdloc007) | Error | A plural family has a form outside the closed set, or no `Other` form. |
| [MDLOC008](#mdloc008) | Error | A key is both a member and the group other keys nest under. |

The analyzer reports only what it can establish from syntax, symbols or declared additional files. Values assembled at runtime stay the runtime's job - see [Runtime validation still applies](#runtime-validation-still-applies).

## Identity

### MDP1001

**Error.** The manifest `id` must be a reverse-domain package id (lowercase, hyphen-separated segments, at least two, joined by dots); `name` and `version` must be present and non-empty. `PluginHostBuilder.Build()` rejects the same values at runtime.

```jsonc
"id": "Hello_Deck"               // MDP1001
"id": "com.example.hello-deck"   // fixed
```

### MDP1002

**Error.** A declared local id (an action's `Id`, a `DeclaredCapability.LocalId`) must be lowercase kebab case starting with a letter, within the length bound, and must never contain `::`, which the host reserves to join an id to its owner.

```csharp
public string Id => "Set_Volume";   // MDP1002
public string Id => "set-volume";   // fixed
```

The state ids an `IStateProviderActionDefinition` returns are **not** covered: they come from a runtime call, not a constant. The host enforces their grammar when a button adopts them, and so does conformance check [MDC0310](/reference/conformance/#mdc0310). No analyzer rule exists for them by design.

### MDP1003

**Error.** The manifest `icon` must end in `.svg`, `.png`, `.jpg`, `.jpeg` or `.webp`.

```jsonc
"icon": "Assets/icon.bmp"   // MDP1003
"icon": "Assets/icon.svg"   // fixed
```

### MDP1004

**Error.** `manifest.json` is the only source of a plugin's id, name, version and icon. An `IPluginIntegration` type that also declares a public `Id`, `Name` or `Version` string, an `IsInitialized` bool, or implements `IIntegrationIconProvider`, is reported. Delete the member.

```csharp
public sealed class PluginIntegration : IPluginIntegration
{
    public string Id => "com.example.hello-deck";   // MDP1004 - delete it
    // ...
}
```

## Registration

### MDP2001

**Error.** Two statically visible declarations of the same capability kind share a constant id, so one of them is unreachable. Rename one.

```csharp
internal sealed class PlayAction : IActionDefinition { public string Id => "play"; /* ... */ }
internal sealed class ResumeAction : IActionDefinition { public string Id => "play"; /* ... */ }   // MDP2001 on both

internal sealed class ResumeAction : IActionDefinition { public string Id => "resume"; /* ... */ } // fixed
```

### MDP2002

**Error.** `ICapabilityHandler.Kind` must return one of the known capability kinds, or no operation can ever reach the handler.

```csharp
public string Kind => "sounds";                  // MDP2002
public string Kind => CapabilityKinds.Events;    // fixed
```

### MDP2003

**Warning.** The capability catalog is built from every registered `ICapabilityHandler`. A handler registered only under its concrete type is constructed but never reaches it.

```csharp
builder.ConfigureServices((_, services) => services.AddSingleton<EventsHandler>());   // MDP2003
builder.RegisterCapabilityHandler<EventsHandler>();                                   // fixed
```

### MDP2004

**Warning.** `RegisterIntegration<T>()` also adds the actions handler and a handler for every other capability interface `T` implements. A raw `AddSingleton` skips all of that.

```csharp
builder.ConfigureServices((_, services) => services.AddSingleton<PluginIntegration>());   // MDP2004
builder.RegisterIntegration<PluginIntegration>();                                         // fixed
```

### MDP2005

**Error.** Every path under `/_macrodeck` belongs to the SDK's health, readiness, info and diagnostics endpoints; the SDK answers first, and `PluginHostBuilder.Build()` rejects a colliding route.

```csharp
endpoints.MapGet("/_macrodeck/status", () => "ok");   // MDP2005
endpoints.MapGet("/status", () => "ok");              // fixed
```

### MDP2006

**Warning.** `[MacroDeckIntegration]` carries only `Platforms` and `EnabledByDefault`, and both are inert out of process: platform gating comes from the manifest's `entrypoints`, and enabled-by-default from whether the plugin declares a config flow. Remove the attribute.

```csharp
[MacroDeckIntegration]   // MDP2006 - remove it
public sealed class PluginIntegration : IPluginIntegration { /* ... */ }
```

## Async

### MDP3001

**Warning.** Inside `ICapabilityHandler.InvokeAsync` and `IActionExecutor.ExecuteAsync`, passing `CancellationToken.None` or `default`, or omitting an optional token parameter, discards the cancellation the caller asked for. Forward `cancellationToken` or `context.CancellationToken`. An overload with no token parameter at all, such as `Task.Delay(int)`, is not reported.

```csharp
await Task.Delay(500, CancellationToken.None);        // MDP3001
await Task.Delay(500, context.CancellationToken);     // fixed
```

### MDP3002

**Warning.** `Task.Result`, `Task.Wait()`, `GetAwaiter().GetResult()` and `Thread.Sleep` block a thread of the shared invocation dispatcher, which has 32 concurrent slots. Applies to `ICapabilityHandler`, `IActionExecutor`, `IConfigFlow` and the other SDK contract types. Await instead.

```csharp
Thread.Sleep(100);            // MDP3002
await Task.Delay(100, ct);    // fixed
```

### MDP3003

**Warning.** An exception from an `async void` member on an SDK contract type crashes the process instead of failing one invocation. Return `Task`.

```csharp
private async void Refresh() => await Task.Delay(1);       // MDP3003
private async Task RefreshAsync() => await Task.Delay(1);  // fixed
```

## Lifecycle

### MDP4001

**Error.** `ICapabilityInvocationContext` exists only inside one invocation's scope. `PluginHostBuilder` validates scopes, so resolving a singleton that takes it fails at startup, every time. Resolve it inside `InvokeAsync` instead. Registrations through `AddSingleton`, `RegisterIntegration` and `RegisterCapabilityHandler` are all checked.

```csharp
internal sealed class Cache
{
    public Cache(ICapabilityInvocationContext context) { }   // MDP4001 on this parameter
}

builder.ConfigureServices((_, services) => services.AddSingleton<Cache>());
builder.ConfigureServices((_, services) => services.AddScoped<Cache>());   // fixed, or resolve it inside InvokeAsync
```

### MDP4002

**Warning.** The supervisor sets `ASPNETCORE_URLS` to a port it has already bound and probes it before the plugin finishes starting. Overriding it makes the health check fail silently and permanently. Reported for `UseUrls`, a `urls` configuration write, and `ASPNETCORE_URLS` in `Properties/launchSettings.json`. Remove the override.

```csharp
webHost.UseUrls("http://127.0.0.1:9000");   // MDP4002 - remove it
```

## Compatibility

`MDP5001`-`MDP5004` let you migrate before an SDK removal becomes a hard break. See [Deprecations](/policies/deprecations/) and [Migrations](/policies/migrations/).

### MDP5001

**Warning.** Reports the same usages as the compiler's `CS0618`/`CS0619`, but only for `[Obsolete]` members declared in `MacroDeck.Sdk`, `MacroDeck.Plugin.Hosting` or `MacroDeck.Plugin.Protocol`, so you can escalate it without escalating every obsolete warning. It stands down where [MDP5002](#mdp5002) applies. No shipped SDK member triggers it today: every obsolete SDK member also carries deprecation metadata.

### MDP5002

**Warning.** Use of an API carrying `[MacroDeckDeprecated]`. The message names the API, the version it was deprecated in, the planned removal version and the guidance. The deprecated widget state members fire it: `WidgetTargetInfo.HasOnOffStates`, `WidgetAppearanceRequest.State` and `WidgetStateSelector`.

```csharp
bool toggle = info.HasOnOffStates;       // MDP5002 (and CS0618)
bool toggle = info.States.Count > 1;     // fixed
```

### MDP5003

**Warning.** A `[MacroDeckDeprecated]` declaration needs a companion `[Obsolete]`, a removal version later than the deprecation version, and non-empty guidance. Reported on the declaration.

```csharp
[MacroDeckDeprecated("3.1.0", "4.0.0", "Use NewHelper instead.")]   // MDP5003: no [Obsolete]
public static void OldHelper() { }

[Obsolete("Use NewHelper instead.")]                                // fixed
[MacroDeckDeprecated("3.1.0", "4.0.0", "Use NewHelper instead.", Replacement = "LegacyHelpers.NewHelper")]
public static void OldHelper() { }
```

### MDP5004

**Error.** The API is still used, and still present, although the declaring assembly has reached its declared removal version. Move to the replacement the message names.

```csharp
LegacyHelpers.OldHelper();   // MDP5004 once the SDK version reaches RemovedIn
LegacyHelpers.NewHelper();   // fixed
```

## Localization

`MDLOC001`-`MDLOC008` come from the source generator that turns `Localization/*.resx` into the typed `Strings` API, so there is nothing separate to enable. See [Localization](/features/localization/) for the workflow.

### MDLOC001

**Error.** A key exists only in a translation. The default file (`Strings.resx`, no culture suffix) is what every translation is checked against and the last fallback. Add the key there.

```xml
<!-- Strings.de.resx; Strings.resx has no OnlyInGerman: MDLOC001 -->
<data name="OnlyInGerman"><value>Nur deutsch</value></data>
```

### MDLOC002

**Error.** Placeholders are named, and the generated method's parameters come from the default language. A translation must use exactly the same set.

```xml
<data name="Greeting"><value>Hello {name}</value></data>   <!-- Strings.resx -->
<data name="Greeting"><value>Hallo {user}</value></data>   <!-- Strings.de.resx: MDLOC002 -->
<data name="Greeting"><value>Hallo {name}</value></data>   <!-- fixed -->
```

### MDLOC003

**Error.** Two entries with the same name in one file leave the winner to file order. Rename or delete one.

```xml
<data name="Greeting"><value>Hello {name}</value></data>
<data name="Greeting"><value>Hi {name}</value></data>   <!-- MDLOC003 -->
```

### MDLOC004

**Error.** A resource comment may declare a placeholder type as a bracketed prefix, such as `[count:int]`. Only types with one unambiguous text form are accepted, and the declared placeholder must appear in the template.

```xml
<data name="DeviceCount"><value>{count} devices found</value><comment>[total:int]</comment></data>   <!-- MDLOC004 -->
<data name="DeviceCount"><value>{count} devices found</value><comment>[count:int]</comment></data>   <!-- fixed -->
```

### MDLOC005

**Error.** The suffix in `Strings.<culture>.resx` must be a well-formed BCP-47 name such as `de`, `de-DE` or `zh-Hans-CN`. The shape is checked because .NET accepts almost any plausible name.

```text
Strings.de_DE.resx   MDLOC005
Strings.de-DE.resx   fixed
```

### MDLOC006

**Error.** Macro Deck's own catalog is a published contract, so a retired key is recorded rather than deleted: code using it still compiles against the SDK that carries the record, and this rule names the replacement. It is this family's counterpart to [MDP5004](#mdp5004).

```text
error MDLOC006: 'Retired' names a Macro Deck localization key that has been removed. Use Current instead.
```

### MDLOC007

**Error.** A `[plural]` entry is one form of a family keyed by the entry's key without its last segment. The form must be `One` or `Other`, and every family needs `Other`.

```xml
<data name="Icons.One"><value>{count} icon</value><comment>[plural]</comment></data>     <!-- alone: MDLOC007 -->
<data name="Icons.Other"><value>{count} icons</value><comment>[plural]</comment></data>  <!-- add this -->
```

### MDLOC008

**Error.** A dotted key becomes a nested class, so a key that is also the prefix of other keys would generate a method and a class with the same name. Rename one.

```xml
<data name="Actions.LogMessage"><value>Log</value></data>              <!-- MDLOC008 -->
<data name="Actions.LogMessage.Name"><value>Write log message</value></data>
```

## Manifest and launch settings

The NuGet package's build props add `manifest.json` and `Properties/launchSettings.json` as additional files when present, which is how [MDP1001](#mdp1001), [MDP1003](#mdp1003) and [MDP4002](#mdp4002) see them. If you consume the analyzer project from source instead, add those files as `AdditionalFiles` yourself.

## Suppression

Use normal Roslyn mechanisms, keep suppressions narrow, and say why:

```csharp
#pragma warning disable MDP3002 // Third-party API has no asynchronous equivalent.
var result = LegacySdk.CallSync();
#pragma warning restore MDP3002
```

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);MDP2003</NoWarn>                            <!-- project-wide -->
  <WarningsAsErrors>$(WarningsAsErrors);MDP5001</WarningsAsErrors> <!-- escalate -->
</PropertyGroup>
```

Do not suppress identity, compatibility or lifecycle diagnostics just to get a build through when the runtime would reject or mishandle the same configuration.

## Runtime validation still applies

Analyzers supplement runtime and package validation. Dynamic ids, provider data, the manifest inside a packed artifact, DI graphs, protocol compatibility and package structure are still validated by hosting and packaging. For package and manifest checks, run `macrodeck-plugin validate` - see the [plugin CLI](/cli/).

## See also

- [SDK packages](/reference/sdk-packages/)
- [Plugin hosting](/reference/plugin-hosting/)
- [Conformance suite](/reference/conformance/)
- [Deprecations](/policies/deprecations/)
- [Localization](/features/localization/)
