# MacroDeck.Plugin.Testing

Test host, fakes and assertions for a plugin built on `MacroDeck.Plugin.Hosting` - written against that
package's public surface only, so it stays accurate across hosting refactors instead of drifting the
way a hand-rolled double would.

## What's here

- `MacroDeckTestHost` - a real loopback host speaking the plugin protocol. Host a plugin in-process
  with `HostAsync`, or launch a real executable with `LaunchAsync`; either way you get a
  `PluginSessionView` to invoke capabilities on and assert against.
- `PluginTestHarness` - drives a plugin's capability handlers directly, with no socket at all, for a
  fast unit test that does not need the wire.
- `MacroDeck.Plugin.Testing.Conformance` - a framework-agnostic suite of protocol-level checks any
  plugin can be run against, from your own test project or from `macrodeck-plugin test`. See
  [the conformance guide](https://docs.macro-deck.app/sdk/conformance/).
- `MacroDeck.Plugin.Testing.Fakes` - in-memory stand-ins for the eight APIs `IIntegrationContext`
  exposes (variables, user variables, config and secrets, deck navigation, scripts, widgets, events,
  notifications) plus action interactions, so an integration can be exercised without a host on the
  other end of any of them.
- `ManualTimeProvider` and `Wait.UntilAsync` - a clock you advance by hand and a polling helper that
  throws `PluginTestTimeoutException` instead of hanging.

## Two authentication modes

`PluginTestCredentials.Managed` and `PluginTestCredentials.SelfRegistering` drive `HostAsync` through
both registration modes a real plugin can run in, the same way `MacroDeck:Plugin:*` configuration and
`MACRO_DECK_PLUGIN_*` environment variables do for a host-launched or self-registering plugin
respectively - both paths derive their key names from `PluginEnvironmentConfiguration.KeysByVariable`
rather than restating them.

## What this package deliberately does not do

It has no dependency on `host/src` and never will - a plugin author testing against it gets exactly the
fidelity a third party gets, nothing the real host does differently. It also references no test
framework: `Wait.UntilAsync` throws rather than asserting, so it works the same under NUnit, xUnit or
anything else.

Licensed under Apache-2.0.
