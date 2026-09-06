---
title: Testing plugins
description: Test plugin behavior with MacroDeck.Plugin.Testing.
---

`MacroDeck.Plugin.Testing` provides test infrastructure for out-of-process plugins without requiring a real Macro Deck installation. It is test-framework agnostic and can be used from NUnit, xUnit, or another .NET test framework.

Use the [Conformance suite](/sdk/conformance/) to verify the standard plugin contract. Use this package for behavior specific to your plugin.

## Install

Reference your plugin project and `MacroDeck.Plugin.Testing` from the test project:

```xml
<ItemGroup>
  <PackageReference Include="MacroDeck.Plugin.Testing" Version="3.0.0" />
  <ProjectReference Include="..\MyPlugin\MyPlugin.csproj" />
</ItemGroup>
```

Add the test framework packages you normally use.

## Choose the right level

| Tool | Use it for |
| --- | --- |
| `PluginTestHarness` | Fast in-process tests of actions, capabilities, integration lifecycle, and host callbacks. |
| `MacroDeckTestHost.HostAsync` | Real HTTP/WebSocket protocol behavior while the plugin runs in-process. |
| `MacroDeckTestHost.LaunchAsync` | End-to-end tests against a built process or packaged plugin artifact. |

Start with the harness. Move to the real protocol host only when serialization, reconnection, cancellation, timeouts, backpressure, or process behavior is part of the requirement.

## PluginTestHarness

The harness builds the plugin through the normal hosting builder but substitutes test-friendly host capabilities and time control.

Typical shape:

```csharp
await using var harness = PluginTestHarness.Create(builder => builder
    .RegisterIntegration<MyIntegration>());

await harness.InitializeIntegrationsAsync();

// Invoke the capability under test and assert observable results.
```

The fake integration context lets tests observe host-facing behavior such as published events, variables, deck navigation, scripts, widgets, notifications, and configuration interactions without mocking the plugin runtime itself.

Use the manual time provider for timeout/backoff behavior instead of sleeping in tests.

## Protocol host

Use `MacroDeckTestHost.HostAsync` when the wire is part of the behavior you need to verify. The plugin connects to a real loopback test server using the production protocol implementation.

This is appropriate for cases such as:

- session negotiation and reconnect behavior;
- protocol cancellation/timeouts;
- serialized capability payloads;
- concurrent invocation behavior;
- host callback round trips.

Do not duplicate protocol conformance tests in every plugin. The conformance suite owns the generic contract.

## Process and package tests

Use `MacroDeckTestHost.LaunchAsync` when process boundaries matter: startup, exit behavior, graceful shutdown, environment handoff, manifest loading, or a packaged `.macroDeckPlugin` artifact.

Keep these tests few because they are slower and more environment-sensitive than harness tests.

## What to assert

Prefer behavior visible to a plugin consumer or the Macro Deck host:

- action results and error codes;
- published events or variables;
- host API calls with meaningful arguments;
- lifecycle behavior across connection replacement;
- cancellation and bounded timeout behavior;
- package/process startup outcomes.

Avoid assertions on internal call order, private helpers, serializer implementation details, or framework internals unless those details are themselves a public compatibility contract.

## Waiting for async behavior

Use the package's wait helpers or manual clock rather than arbitrary real-time delays. Tests should have bounded waits and fail with a useful reason when the expected state never appears.

## Fakes and real boundaries

The package fakes the Macro Deck host boundary. Your plugin can still use normal fakes/test doubles for external services such as provider APIs, filesystems, or operating-system integrations.

Do not mock every SDK type just because it is injectable. Prefer the provided test host/context so your tests exercise the same integration boundary the real plugin uses.

## Related documentation

- [Conformance suite](/sdk/conformance/)
- [Plugin hosting](/sdk/hosting/)
- [Capabilities](/sdk/capabilities/)
- [Debugging](/guides/debugging/)
