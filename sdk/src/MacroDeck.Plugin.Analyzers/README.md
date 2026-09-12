# MacroDeck.Plugin.Analyzers

Roslyn analyzers for [Macro Deck](https://github.com/Macro-Deck-App/Macro-Deck) plugin authors. Add
the package and mistakes that would otherwise only surface at startup, or only inside a real host
session, are caught while you type:

```xml
<PackageReference Include="MacroDeck.Plugin.Analyzers" Version="..." PrivateAssets="all" />
```

`PrivateAssets="all"` keeps it a build-time-only dependency - it never becomes part of your plugin's own
published output or a dependency your users need to know about.

## Diagnostics

| Id | Severity | Fires on |
|---|---|---|
| [MDP1001](https://docs.macro-deck.app/reference/analyzers/#mdp1001) | Error | `manifest.json` declares an `id` that is not a valid package id, or no `name`/`version` |
| [MDP1002](https://docs.macro-deck.app/reference/analyzers/#mdp1002) | Error | A declared local id (an action's `Id`, a `DeclaredCapability.LocalId`) that is not a valid declared local id |
| [MDP1003](https://docs.macro-deck.app/reference/analyzers/#mdp1003) | Error | `manifest.json` declares an `icon` whose file extension has no known media type |
| [MDP1004](https://docs.macro-deck.app/reference/analyzers/#mdp1004) | Error | An `IPluginIntegration` type restates manifest-owned identity (a public `Id`/`Name`/`Version`/`IsInitialized`, or an `IIntegrationIconProvider` implementation) |
| [MDP2001](https://docs.macro-deck.app/reference/analyzers/#mdp2001) | Error | Two statically visible declarations of one capability kind sharing a constant id |
| [MDP2002](https://docs.macro-deck.app/reference/analyzers/#mdp2002) | Error | `ICapabilityHandler.Kind` returning a constant outside the known capability kinds |
| [MDP2003](https://docs.macro-deck.app/reference/analyzers/#mdp2003) | Warning | An `ICapabilityHandler` registered with `AddSingleton<T>()` but never also as `ICapabilityHandler`, so it never reaches the catalog |
| [MDP2004](https://docs.macro-deck.app/reference/analyzers/#mdp2004) | Warning | An `IPluginIntegration` registered with raw `AddSingleton` instead of `builder.RegisterIntegration<T>()` |
| [MDP2005](https://docs.macro-deck.app/reference/analyzers/#mdp2005) | Error | A mapped route whose constant path falls under the SDK's reserved `/_macrodeck` prefix |
| [MDP2006](https://docs.macro-deck.app/reference/analyzers/#mdp2006) | Warning | `[MacroDeckIntegration]` applied to a type implementing `IPluginIntegration`, where both of its properties are inert |
| [MDP3001](https://docs.macro-deck.app/reference/analyzers/#mdp3001) | Warning | A call inside a capability or action handler that does not forward the `CancellationToken` already in scope |
| [MDP3002](https://docs.macro-deck.app/reference/analyzers/#mdp3002) | Warning | `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` or `Thread.Sleep` inside a type implementing `ICapabilityHandler`, `IActionExecutor` or `IConfigFlow` |
| [MDP3003](https://docs.macro-deck.app/reference/analyzers/#mdp3003) | Warning | `async void` on a member of a type implementing an SDK contract |
| [MDP4001](https://docs.macro-deck.app/reference/analyzers/#mdp4001) | Error | A singleton-registered type whose constructor takes `ICapabilityInvocationContext`, which only resolves inside one invocation's scope |
| [MDP4002](https://docs.macro-deck.app/reference/analyzers/#mdp4002) | Warning | The plugin overriding its own listener URL (`UseUrls`, `Configuration["urls"]`, or `ASPNETCORE_URLS` in `launchSettings.json`) |
| [MDP5001](https://docs.macro-deck.app/reference/analyzers/#mdp5001) | Warning | Use of an `[Obsolete]` member from `MacroDeck.Sdk`, `MacroDeck.Plugin.Hosting` or `MacroDeck.Plugin.Protocol` |

Every diagnostic has a stable id, so a rule you disagree with can be turned off or escalated on its own -
`<NoWarn>MDP2003</NoWarn>` or `-warnaserror:MDP5001` - without affecting the others.

Most rules only act on what they can prove statically. A path, id or URL assembled from a literal or a
`const` is checked; one built at runtime from a variable is usually not - guessing at a runtime value
would trade a real mistake caught for an occasional wrong guess, which is not a trade this package makes.
MDP4002's `UseUrls(...)` check is the one exception: it flags the call itself regardless of whether its
argument is a literal, because the supervisor has already assigned the plugin's listener address before
the process starts, and any override - however it is computed - defeats the health probe the same way.

## What is not here

No code-fix providers. Every rule here is meant to prove itself against real plugins first; a code fix is
a second, separable commitment this package does not make yet.

See [the plugin development documentation](https://docs.macro-deck.app/introduction/quickstart/).

Licensed under Apache-2.0.
