---
title: Capabilities
description: Capability overview for Macro Deck integrations and plugins.
---

Integrations expose actions through the base SDK contract and opt into additional features through capability interfaces. Out-of-process plugins use the same capability contracts through `MacroDeck.Plugin.Hosting`.

Use this page to choose the capability you need. Use IntelliSense and the SDK XML documentation for exact member signatures.

## Overview

| Capability | Main SDK contract |
| --- | --- |
| Actions | `IActionDefinition`, `IActionExecutor` |
| Dynamic action options | `IDynamicOptionsActionDefinition` |
| Action button state providers | `IStateProviderActionDefinition` |
| Action button icon providers | `IIconProviderActionDefinition` |
| Music players | `IMusicPlayerProvider`, `IMusicPlayer`, `ICatalogMusicPlayer` |
| Weather | `IWeatherProvider`, `IWeatherStation` |
| Variables | `IVariableProvider` |
| Configuration flows | `IConfigFlowProvider`, `IConfigFlow` |
| Virtual profiles | `IProfileProvider` |
| Device providers | `IDeviceProvider`, `IDeviceProviderContext` |
| Layout providers | `ILayoutProvider`, `ILayoutProviderContext` |
| Integration issues | `IIntegrationIssueProvider` |
| Macro Deck UI | `IUiProvider`, `IUiSession` |
| Events | `IEventProvider`, `IEventPublisher` |
| Deck navigation | `IIntegrationContext.Deck` |
| Scripts | `IIntegrationContext.Scripts` |
| Widgets | `IIntegrationContext.Widgets` |
| Notifications | `IIntegrationContext.Notifications` |

## Capability ids

An integration or plugin owns the capabilities it declares. APIs normally accept a local id; Macro Deck qualifies it with the authenticated owner identity.

Declared ids such as action, event, and variable definition ids use stable lowercase kebab-case. Do not pass an already-qualified `owner::local` value to an API that expects a local id. Released ids are compatibility-sensitive because profiles and configuration can persist them.

Runtime resource ids may use a broader grammar when they come from provider state. They still must not contain the reserved `::` separator.

## Actions

`IIntegration.Actions` exposes `IActionDefinition` instances. Each definition describes its parameters and creates an `IActionExecutor`.

Executors return an `ActionResult`:

- `Success` means the requested operation completed successfully.
- `Failed` means the operation did not complete and should include a stable error code plus a user-safe message when useful.
- `Accepted` is for work that was accepted but cannot yet be confirmed as complete.

An action that is also the configured `IStateProviderActionDefinition` may return `Success(expectedStateId)` or `Accepted(message, expectedStateId)`. The id must be one it advertises from `GetActionStateAsync`. The host may show that state briefly while waiting for the provider to confirm it; failures and results without an expected id keep normal polling behavior.

Do not return success merely because a request was sent. If the provider can confirm completion, wait for that confirmation or report failure.

Use `IUiConfigurableActionDefinition` when an action's configuration is better expressed as a Macro Deck UI tree than as a flat parameter list; the action's descriptor reports this as `configuresWithUiTree`. `Parameters` stays authoritative - it is what a client that cannot render a tree configures through, and what the host persists into. See [Serving a configuration view](/sdk/ui/views/configuration/).

Use `IDynamicOptionsActionDefinition` when an editor choice depends on provider state. When the provider cannot answer right now, set `DynamicOptionsResult.Error` to a localized message explaining why the list is empty instead of returning an unexplained empty list.

Use `IStateProviderActionDefinition` when a configured action instance can drive an action button's N-state appearance - it declares the states an instance can be in and which one is current; the host decides which instance, if any, is a given button's provider. It is free-standing, not an extension of `IActionDefinition`, and its `GetActionStateAsync` must stay side-effect free and tolerate a partially filled parameter set, since it is also polled while the action is still being configured.

For the state labels themselves, reach for the `States.*` family in `MacroDeckStrings` - `On`, `Off`, `Muted`, `Visible`, `Recording`, `Unavailable` and similar - rather than declaring your own copies; see [Reuse `MacroDeckStrings`](/sdk/localization/#reuse-macrodeckstrings-instead-of-duplicating-common-strings).

Each declared state may carry an `ActionStateAppearance` in `DefaultAppearance`: a suggested label, background colour, label colour and icon id. The host applies it only to a state a button is adopting for the first time, so re-reading a provider never restyles what the user has already configured. A state whose appearance suggests no label is labelled with the state's own display label, so a provider gets readable buttons without naming every state twice; suggest an empty label for an icon-only state.

Use `IIconProviderActionDefinition` when a configured action instance should own an Action Button's currently rendered icon - album artwork, an avatar, weather imagery - independently of whether the same instance also implements `IStateProviderActionDefinition`. It is free-standing, not an extension of `IActionDefinition`, and the host decides which instance, if any, is a given widget's provider; enabling one capability never enables the other.

`GetActionIconAsync` answers for the *configured instance*, from its own `parameters`, the same way `GetActionStateAsync` does, and must stay side-effect free and tolerate a partially filled parameter set. It owns the icon currently rendered rather than one icon per state, so it is resolved once per widget, and answers one of three things: `null` when it cannot answer right now, which falls back to the widget's own configured icon; an explicit `ActionIconSnapshot.NoIcon`, which renders nothing; or a `Version` identifying the current image, resolved either through a host `Reference` or as bytes `GetActionIconContentAsync` returns only when `Version` changes. Unlike a state provider, unavailability always falls back to the configured icon rather than holding the last one shown - there is a meaningful default to fall back to, so a widget never shows a stale image.

The provider overrides the rendered icon at render time only: the widget's own configured icon (per state, where applicable) is never rewritten, and reappears the moment the provider is removed or disabled. Set Icon fails on a widget with an active icon provider for the same reason it fails on a state-controlled one; the icon property alone is dropped from a mixed patch, so the rest still applies. See [Capability parity](/sdk/capability-parity/) for the poll/push story a plugin sees, and [Device providers](/sdk/devices/) for how a provider-controlled icon reaches a physical device.

## Configuration flows

`IConfigFlowProvider` exposes guided configuration or authentication flows. A flow returns steps, receives submitted values, and eventually completes or fails.

Use the flow context for host-provided services such as OAuth coordination rather than implementing parallel callback infrastructure. Keep secrets in the SDK/host mechanisms intended for credentials and avoid placing them in logs or user-visible errors.

`IUiConfigFlowProvider` and `IUiConfigFlow` let a flow additionally render itself as a Macro Deck UI tree; the flow's `describe` reports this as `servesConfigUiTree`. The declared step is still served either way, and its fields remain how the host learns which submitted values are secret - see [Serving a configuration view](/sdk/ui/views/configuration/).

For out-of-process plugin setup, see [Plugin hosting](/sdk/hosting/) and [Authentication](/sdk/authentication/).

## Variables

`IVariableProvider` is one catalog of variable definitions, addressable by definition id. A definition
declares what the value *is* - its type, its unit, what it means - and, optionally, that the owner
accepts writes to it.

Each definition declares when it comes into existence. `VariableMaterialization.Eager` is registered
and polled from the moment the provider is initialized; `OnDemand` is present in the catalog but
becomes a registry entry only once a user binds it. A provider whose set is small and known ahead of
time declares it eagerly and implements nothing else; one whose set is a runtime resource space -
Home Assistant entities, OBS sources, MQTT topics - sets `SupportsCatalog` and serves a browsable
catalog instead. `SupportsCatalog` is off by default, so a provider that leaves it alone is never
asked to discover and never appears as a catalog source.

See [Variables](/sdk/variables/) for materialization, attributes, the write capability, discovery,
paging, push versus poll, binding lifetime, and how all of it crosses the plugin protocol.

## Events

`IEventProvider` declares events an integration can emit. `IEventPublisher` publishes occurrences through the host event system.

Event definition ids are stable public identities. Dynamic option providers may supply runtime choices for configuration and payload parameters alike, without changing the event definition itself.

Payload parameters are not only documentation. An event's `PayloadParameters` are ordinary `ActionParameter` declarations, and Macro Deck reuses that metadata when a user writes a condition against `$event`: the value being compared against is authored with the control the payload parameter's own type implies, while the condition still stores the raw value the occurrence carries. Declare a payload value the integration can enumerate the same way its matching filter is declared, so a user picks a friendly name instead of pasting an id.

`EventOptionsContext` names only the event and the parameter, so a provider cannot tell a request for a configuration parameter from one for a payload parameter of the same name. Declare the same name in both lists only where the same options answer for both.

## Provider capabilities

Music, weather, and profile capabilities expose provider instances behind neutral SDK contracts. Keep provider-specific transport details behind the integration implementation and return the SDK model expected by Macro Deck.

Capability methods that can contact an external provider should honour cancellation and report provider failures without blocking unrelated UI or transport work.

## Device providers

`IDeviceProvider` registers hardware or custom clients with Macro Deck's own device model, keyed by a
stable provider-local id so a device survives reconnects and restarts as the same device. See
[Device providers](/sdk/devices/).

A provider is driven from the plugin side for registration, so the host-to-provider direction of the
`device-provider` capability only has to describe the provider, re-read its catalogue after a
reconnect, and - from capability version 2 - open, push to, and close a device's rendering session:

| Operation | Purpose |
| --- | --- |
| `describe` | The provider's declared name and capability version. |
| `devices` | The provider's current device catalogue, re-read after a reconnect. |
| `session.open` | Opens a device's session and hands over the first surface to render. Version 2 only. |
| `session.surface` | Pushes a new, complete surface to an already-open session. Version 2 only. |
| `session.close` | Closes an open session. Version 2 only. |

Registration itself - `register`, `update`, `presence`, `unregister` - along with reporting a
hardware interaction and fetching an icon, travels the other way as the `devices` host API:

| Operation | Purpose |
| --- | --- |
| `register` | Registers a device, or re-registers a known provider-local id as the same device. |
| `update` | Refreshes a registered device's metadata. |
| `presence` | Reports whether a registered device is currently reachable. |
| `unregister` | Withdraws a device from this session; the device itself is retained. |
| `interaction` | Reports a hardware interaction from an open device session. |
| `icon` | Fetches icon bytes referenced by a device's current surface, over the `host.asset.*` pipeline. |
| `widget-icon` | Fetches the bytes behind a widget's currently rendered action-icon-provider icon, over the `host.asset.*` pipeline. |
| `close` | Closes an open device session at the provider's own request. |

## Layout providers

`ILayoutProvider` registers layout descriptors - the regions, geometry and rendering capabilities of a
device or client surface - independently of device registration itself. A device points at a registered
layout through `DeviceDescriptor.LayoutReference`. See [Layout providers](/sdk/layouts/).

A provider is driven from the plugin side for registration, so the host-to-provider direction of the
`layout-provider` capability only has to describe the provider and re-read its catalogue after a
reconnect:

| Operation | Purpose |
| --- | --- |
| `describe` | The provider's declared name. |
| `layouts` | The provider's current layout catalogue, re-read after a reconnect. |

Registering and withdrawing a layout travels the other way as the `layouts` host API:

| Operation | Purpose |
| --- | --- |
| `register` | Registers a layout, or replaces one already registered under the same provider-local id. |
| `unregister` | Withdraws a layout. Devices still referencing it keep their last-resolved geometry rather than losing their constraint. |

## Folder view providers

`IFolderViewProvider` offers complete renderings of a folder, in place of Macro Deck's built-in widget
grid. A folder stores a registered view's qualified id and its own opaque configuration for it. See
[Folder views](/sdk/folder-views/).

Registration is driven from the plugin side, so the host-to-provider direction of the
`folder-view-provider` capability only has to describe the provider and re-read its catalog after a
reconnect:

| Operation | Purpose |
| --- | --- |
| `describe` | The provider's declared name and its current catalog. |
| `folder-views` | The provider's current folder view catalog, re-read after a reconnect. |

Registering and withdrawing a view travels the other way as the `folder-views` host API:

| Operation | Purpose |
| --- | --- |
| `register` | Registers a folder view, or replaces one already registered under the same provider-local id. |
| `unregister` | Withdraws a folder view. Folders still using it keep their stored id and configuration and show a placeholder until it returns. |

The views themselves are served over the `ui` capability, like every other Macro Deck UI surface.

## Widget type providers

`IWidgetTypeProvider` offers deck widget types beside Macro Deck's own. A widget stores a registered type's
qualified id and its own data for it. See [Widget types](/sdk/widgets/).

Registration is driven from the plugin side, so the host-to-provider direction of the
`widget-type-provider` capability only has to describe the provider and re-read its catalog after a
reconnect:

| Operation | Purpose |
| --- | --- |
| `describe` | The provider's declared name and its current catalog. |
| `widget-types` | The provider's current widget type catalog, re-read after a reconnect. |

Registering and withdrawing a type travels the other way as the `widget-types` host API:

| Operation | Purpose |
| --- | --- |
| `register` | Registers a widget type, or replaces one already registered under the same provider-local id. |
| `unregister` | Withdraws a widget type. Widgets already using it keep their stored type and data and wait for it to return. |

The widgets themselves are drawn, previewed and configured over the `ui` capability, like every other Macro
Deck UI surface.

## Macro Deck UI

`IUiProvider` serves declarative UI trees to Macro Deck. The host opens one *session* per place a tree is rendered, and routes between your provider and whichever clients attach; a provider never addresses a client, and a client never addresses a provider.

`Surfaces` declares which surface kinds and session modes the provider serves, and is what `describe` answers with. `CreateSessionAsync` receives the surface being rendered and returns an `IUiSession`, or `null` to decline a surface kind you do not serve - declining is a normal answer, not an error. The session is expressed in `MacroDeck.Ui.Model` terms (`BuildTree`, `DrainPatches`, `Changed`, `Faulted`, `Dispatch`) so a provider can serve a tree without depending on the `MacroDeck.Ui` DSL; a DSL user forwards these members to their `UiView`.

Over the plugin protocol the same contract is the `ui` capability kind, invoked by the host:

| Operation | Purpose |
| --- | --- |
| `describe` | The surfaces this provider can serve, the UI model version it speaks, and the [preview scenarios](/sdk/ui/views/developer-preview/) it declares. `previews` is optional: a plugin built against an SDK that predates it omits the key, and the host reads that as none. |
| `session.open` (config) | A `config` surface carries the entry point being configured in its surface attributes - `integration-config` with the config flow session, `action-config` with the action id and the instance's stored parameters, `folder-view-config` with the folder and its view, or `widget-config` with the widget id, type and stored configuration. `MacroDeck.Plugin.Hosting` routes these to `IUiConfigFlow`/`IUiConfigurableActionDefinition` before it consults `IUiProvider`. |
| `session.open` | Open a session for one surface. The session id is host-issued; a provider never mints one. |
| `session.open` (developer preview) | A `developer-preview` surface names one registered preview scenario in its surface attributes. `MacroDeck.Plugin.Hosting` builds that scenario and never consults `IUiProvider`, so a production provider is unreachable from a preview. |
| `session.snapshot` | Produce the session's current full tree. The tree does not return on the result - it arrives as a separate `host.invoke ui/snapshot`, so one delivery path serves a first attach and a resync alike. |
| `session.event` | A client acted on a node. `clientId` says which one, and is meaningful only for a shared session. |
| `session.close` | The host is ending this session. |
| `modal.result` | How a modal this plugin opened ended. Host-to-plugin because the wait is unbounded by a person - see [action modals](/sdk/ui/views/modal/). |

Trees, patches and faults travel the other way through the [`ui` host api](/reference/websocket/#host-callbacks).

Like the other provider-shaped capabilities, `ui` declares the single local id `provider`: the capability *is* the plugin's one UI provider, so there is no per-instance identity to name. The host invokes `kind: "ui", localId: "provider"`.

For the session lifecycle, the limits and what a limit trip looks like from a provider, see [Serving a view](/sdk/ui/views/sessions/).

## Migrations

`IMigrationProvider` takes an integration's own setup over from another application, so someone arriving
from Macro Deck 2 keeps the buttons and the connections they already had. The capability is a *list*:
one integration commonly reads several applications, so it declares one `IIntegrationMigration` per
application, each naming its `MigrationSource`.

```csharp
public sealed class ObsIntegration : IIntegration, IMigrationProvider
{
    public IReadOnlyList<IIntegrationMigration> Migrations { get; } = [new ObsMacroDeck2Migration()];
}
```

A migration declares what it claims in the source application - `ClaimedActionSources` for whatever that
application uses to say which plugin an action came from, `ClaimedSettingsSources` for its stored
per-plugin settings and credentials - and answers two questions about what the host found:

- `MigrateActionAsync` translates one foreign action, or returns `null` when there is no equivalent.
  Returning `null` is a normal answer, not a failure: the host keeps the action as a placeholder carrying
  its original configuration, which is better than an action that quietly does something else.
- `MigrateConfigurationAsync` turns a foreign plugin's settings and its already-decrypted credentials
  into configuration entries. Credentials may be empty - the user can decline to decrypt them, and an
  application that ties them to the machine that wrote them cannot open a folder copied off another one -
  so never return an entry that would look configured but cannot work.

Anything a translation could not carry across exactly goes in `Warnings` as `LocalizedText`, so it reads
in the language of whoever opens the migration wizard.

Both members are asynchronous because an out-of-process plugin answers over its connection. Over the
plugin protocol this is the `migration` capability kind, declared at the single local id `provider` like
the other provider-shaped capabilities, with the source named in each invocation's arguments:

| Operation | Purpose |
| --- | --- |
| `describe` | Every application this plugin migrates from, and what each one claims. A source name the host does not recognise is ignored rather than refused, so a plugin built against a later SDK stays usable. |
| `migrate-action` | Translate one foreign action. `translated: false` is the "no equivalent" answer. |
| `migrate-configuration` | Turn a foreign plugin's settings and credentials into configuration entries. Secret values travel separately from the plain ones so the host can store each in its secret store and leave only a reference in the entry. |

Reading the foreign installation itself is the host's work, never the plugin's: the host finds the
files, decrypts what it can, and asks whoever claims what it found.

## Integration issues

`IIntegrationIssueProvider` exposes actionable integration problems such as missing configuration or unavailable dependencies. Use issues for state the user can understand and act on; use logs for diagnostic detail.

## Host APIs on `IIntegrationContext`

The integration context exposes host-owned capabilities such as deck navigation, scripts, widgets, notifications, variables, configuration, and events. In an out-of-process plugin these calls cross the plugin protocol, so avoid treating them as free local operations in hot loops.

## Compatibility

Capability contracts are public SDK surface. Evolve them additively and follow the [compatibility policy](/policies/compatibility/) and [deprecation policy](/policies/deprecations/).

For exact wire differences between in-process integrations and plugins, see [Capability parity](/sdk/capability-parity/). For a complete working setup, see [Contributing an integration](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/development/contributing-integrations.md).
