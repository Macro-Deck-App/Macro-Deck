---
title: Compatibility policy
description: Which Macro Deck plugin surfaces are frozen, what counts as a breaking change, how contracts are evolved additively, and how protocol majors are negotiated.
---

A plugin ships as a compiled assembly, built against an SDK version and updated on a schedule its author
controls, not Macro Deck. So the promise is: **a plugin compiled against an older SDK keeps loading and
behaving the same against a newer host - no recompile, no behaviour change.** Every contract below is
frozen while it is public and not marked `[Obsolete]`, and changes only through the process under
[how contracts change](#how-contracts-change). The few deliberate exceptions to that promise are listed
under [behaviour changes that moved no version](/ui/reference/compatibility/#behaviour-changes-that-moved-no-version).

## What is covered

| Surface | Examples | Promise |
| --- | --- | --- |
| SDK packages | `MacroDeck.Sdk`, `MacroDeck.Plugin.Hosting`, `MacroDeck.Plugin.Testing`, `MacroDeck.Plugin.Packaging`, `MacroDeck.Plugin.Serilog`, `MacroDeck.Plugin.Cli`, `MacroDeck.Localization` | No public type or member is removed or changed incompatibly, source or binary. |
| UI model | `MacroDeck.Ui`, `MacroDeck.Ui.Model`, `MacroDeck.Ui.Testing` | Frozen like the SDK; the UI model's wire major is negotiated separately - see [the UI model majors](#ui-model-majors). |
| Protocol | `MacroDeck.Plugin.Protocol`: the envelope, DTOs, message types, error codes | Append-only within a protocol major; a break needs a new major. |
| Plugin HTTP and WebSocket | `/api/plugins/*`, `/plugins/ws` | Existing endpoints keep their shape and meaning - see [the protocol reference](/reference/protocol/). |
| Capability and host API catalogues | `device-provider`/`devices`, `layout-provider`/`layouts`, `folder-view-provider`/`folder-views`, `widget-type-provider`/`widget-types`, `screensaver-provider`/`screensavers`, `messaging`/`messaging`, `video-stream-provider`/`video-streams`, `calendar`, `config-entries`, `battery-provider`/`batteries`, `email-inbox`, `companion-device-entries`, `locale`, `companion-devices`, `music-players`, `desktop-notifications`, `event-bindings`, `http` | Names stay; see [capability operations](/reference/protocol/#capability-operations). |
| Manifest and package format | `manifest.json`, the `.macroDeckPlugin` package | An existing manifest and package keep installing - see [the manifest reference](/reference/manifest/). |
| Analyzer diagnostic ids | `MDP1001`, …, the `MDLOC` family `MDLOC001`-`MDLOC008` | An id keeps its meaning and is never reused - see [analyzers](/reference/analyzers/). |
| Conformance check ids | `MDC0305`, … | Stable, so you can gate CI on them - see [conformance](/reference/conformance/). |
| Plugin `.resx` contract | the default-language file, `Strings.<culture>.resx`, named placeholders, the bracketed parameter type in a `<comment>` | A plugin's `Localization/*.resx` keeps compiling - see [Localization](/features/localization/). |
| MCP tool contract | `macrodeck-mcp/1`: tool names, argument names, result fields, error codes | Additive-only within the contract version - see [the MCP server reference](/reference/mcp/). |
| Macro Deck's localization catalog | `macrodeck:Common.Save`, … exposed as `MacroDeckStrings` | **Additive-only**: a shipped key is never deleted outright. |

Retiring a catalog key follows the same idea as [SDK deprecation](/policies/deprecations/): the key
keeps resolving, a plugin referencing it gets [MDLOC006](/reference/analyzers/#mdloc006) naming the
replacement, and the record stays for as long as the deprecation lifecycle requires.

Background: [ADR 0026](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0026-plugin-protocol-and-sdk-boundary.md)
(protocol and SDK boundary) and
[ADR 0029](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0029-plugin-packaging-installation-and-supervision.md)
(packaging).

## Breaking or not?

**Source *and* binary compatibility both count.** A plugin ships compiled, so a change that recompiles
cleanly can still break it at run time.

| Change | Verdict |
| --- | --- |
| Remove or rename a public type, member, parameter, enum value, constant, message type, field or error code | Break |
| Change a parameter or return type, or parameter order | Break |
| Add a parameter to a public method, **even with a default value** | Binary break |
| Change `class` to `struct`, or add or narrow a generic constraint | Break |
| Add a member to a public interface without a default implementation, or make an existing member abstract | Break for every implementer |
| Add a member to a public interface **with** a default implementation | Allowed |
| Add a new overload | Allowed |
| Add a value to a C# enum | Allowed by .NET's own rules - give a `switch` over a Macro Deck enum a default branch |
| Add an optional DTO or manifest field | Allowed - unknown fields are ignored on read |
| Rename a JSON property, change its type, or change an enum's wire representation | Break |
| Make an optional field required, tighten validation, or change a default a plugin relied on | Break |
| Change an error code's meaning, even with the same spelling | Break |
| Change ordering guarantees, id formats (the `integrationId::localId` shape), thrown exception types, timing or lifecycle contracts | Break |

The two that surprise people most:

**A parameter with a default is a binary break.** The compiler bakes the full argument list into the
call site, so an already-compiled plugin calls a method that no longer exists and gets a
`MissingMethodException`.

```csharp
// illustrative method
// before
public Task ShowAsync(string text);

// after - recompiles cleanly, breaks every compiled caller
public Task ShowAsync(string text, TimeSpan? duration = null);

// instead - keep the old method, add an overload
public Task ShowAsync(string text);
public Task ShowAsync(string text, TimeSpan duration);
```

**A changed meaning is a break.** Nothing about the spelling changes, but a plugin branching on it now
branches wrongly.

```text
hypothetical: an error code that meant "retry later" starts to mean "give up"
same spelling, same field - still a break, because plugins that retry now retry wrongly
```

## How contracts change

The rule is *add, do not change*.

**New overloads**, rather than new parameters on an existing method - see the example above.

**New optional members** with a sensible default, rather than required ones.

**New optional DTO fields.** Unknown fields are ignored on read, so an older plugin simply does not see
what it was not built for.

```jsonc
// illustrative: a reader built before displayName existed reads this exactly as {"id":"a"}
{ "id": "a", "displayName": "Kitchen" }
```

**New message types.** An unrecognised message type produces `UNKNOWN_MESSAGE_TYPE` and is never fatal:
the peer reports it and carries on. That makes every additive protocol change backward-compatible by
construction.

**New capability interfaces** a plugin opts into, rather than new members on an existing one. An existing
interface is extended only through a default interface implementation.

```csharp
// illustrative interface names
// not this: every existing implementer stops loading
public interface IStateProvider { Task RefreshAsync(); }

// this: a new opt-in interface
public interface IRefreshableStateProvider : IStateProvider { Task RefreshAsync(); }

// or, on the existing interface, a default implementation
public interface IStateProvider { Task RefreshAsync() => Task.CompletedTask; }
```

**Anything that has to go follows the deprecation lifecycle.** It is marked with `[Obsolete]` *and*
`[MacroDeckDeprecated]` carrying a declared removal version, registered so it appears in the registry
table, and it **keeps working unchanged** the whole time. Removal happens only in the major release its
`RemovedIn` names - never earlier, never in a minor or a patch. You are told at compile time as
[MDP5002](/reference/analyzers/#mdp5002), with the removal version and replacement in the message, and at
run time in the desktop app, where the person running your plugin sees the same finding under the same
id. See [deprecations](/policies/deprecations/) and
[ADR 0037](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0037-sdk-deprecation-is-declared-metadata.md).

**A wire break means a new protocol major, with the previous one still served** - see
[protocol majors](#protocol-majors).

## The 3.0 metadata retype

One break was taken deliberately before 3.0 shipped, and it is the only non-additive change on this page.
The members carrying an action's, a parameter's and a config-flow step's user-facing text changed type
from `string` to `LocalizedText`:

`IActionDefinition.Name`/`.Description`, `ActionParameter.Label`/`.Description`/`.Placeholder` and its
factories, `ActionParameterOption.Label`, `ActionStateDefinition.Label`,
`WidgetTargetOptions.Label`/`.Description`, `ConfigFlowStep.Title`/`.Description`, `ConfigFlowLink.Label`,
`ConfigFlowCopyValue.Label`, `ConfigFlowInstruction.Text`, and `IIntegration.Name`.

```csharp
Label = "Client ID"                       // assigning: unchanged, LocalizedText converts from string
public string Name => "Timer";            // implementing: no longer compiles
public LocalizedText Name => "Timer";     // implementing: fixed
```

Implementing one of those members is a source and a binary break. It was affordable because 3.0 had not
been released and no plugin existed to break; the alternative was a permanent parallel surface (`Name`
beside `NameLocalized`). After 3.0 this page's ordinary rules apply to these members like any other.

Two things did **not** change:

- **`VariableDefinition.Name` is still a `string`.** It is the variable's identity - the host derives a
  definition id from it and substitutes it into a template placeholder - so it cannot differ by language.
  Localized display text goes in the additive `DisplayName` property.
- **Descriptor metadata is localized from protocol v3.** `ActionDescriptorDto.Name`,
  `ActionParameterDto.Label`, the event and issue descriptors and the config-flow DTOs carry
  `LocalizedText`, so an out-of-process plugin's action names render in the reader's language. Below v3
  the SDK resolves them in the plugin's own default language first, because an older host would reject an
  object where it expects a string - the negotiated version decides, and v1 and v2 are still served. A
  plugin's text inside a Macro Deck UI tree was never affected; it always travelled as a reference.
  `ConfigFlowResultDto.EntryTitle` stays a plain string on purpose: the host stores it as the configured
  entry's name, which the user then owns and can rename.

## Secret resolution in flows

A flow refers to a secret as `{ "$secret": "<id>" }`. The host decrypts such a reference only where the
action **declares** a `Secret` or `Password` parameter, read from the action's own parameter list, never
from the type a stored block names. That includes declared children of an `Object` parameter and the
`ItemTemplate` of an `Array` parameter. Anywhere else the reference arrives as an empty string.

- A plugin that declares its secret with `ActionParameter.Secret` or `ActionParameter.Password` receives
  the plaintext exactly as before.
- A reference in a parameter the plugin declares with any other type, or does not declare at all, no
  longer reaches it. A declared parameter is converted by its declared type.
- Secrets nested in a declared `Object` or `Array` parameter are now decrypted; before they arrived as the
  reference object.
- A config-flow submit that carries a secret reference the entry does not already hold under that key is
  refused with an error.

This was treated as hardening, not a contract break: no published plugin read a secret through an
undeclared parameter. See [secrets in configuration](/policies/security/#secrets-in-configuration).

## Versions

| Version | Scheme | Breaks allowed |
| --- | --- | --- |
| SDK packages | Ordinary semver | Only at an SDK major, and only removals whose `RemovedIn` names that major |
| Plugin protocol | One monotonically increasing integer major, not semver | Only with a new major; the previous one stays served |
| Capability, per kind | Integer min/max range per kind | Only with a new capability version; negotiated separately from the protocol |
| UI model (`UiModelVersions`) | Integer major | Only with a new major; negotiated separately from the protocol |

### Protocol majors

Unknown fields are ignored and unknown message types are reported rather than fatal, so every additive
change is backward-compatible without a version change. A bump therefore only ever means *breaking*.

Today `minimum` is `1` and `current` is `3`.

| Major | Changes exactly one thing | Older majors |
| --- | --- | --- |
| `2` | A widget appearance change names its states by stable id instead of the fixed `Current`/`On`/`Off`/`Both` selector, because a button may have any number of states. | `1` fully served - see the [migration guide](/policies/migrations/) for what a version `1` plugin sees when it restyles a three-state button. |
| `3` | Action names, parameter labels and config-flow text may be a `{"$localized":…}` reference the reader's client resolves, instead of a plain string. | `1` and `2` fully served. A plugin below `3` must keep sending a plain string, since an older host would reject the object. |

What did **not** need a major:

- Actions gained the ability to supply a button's states: an additive descriptor field plus an additive
  capability operation, so a version `1` plugin can be a state provider. Additive changes never move the
  major, however visible the feature.
- The `variables` catalog half - `discover`, `resolve`, `subscribe` and the `variable-values` host API's
  `value`/`invalidate` - is ordinary `capability.invoke`/`capability.result` and
  `host.invoke`/`host.result` traffic. Variable definition attributes, a `write` capability and a `set`
  operation *did* change a payload plugins already send, so the **capability** version moved from `1` to
  `2` while `ProtocolVersions.Current` stayed at `3`.
- Widget appearance gained an accent colour: `WidgetAppearancePatch.AccentColor`,
  `WidgetAppearanceProperty.AccentColor` and an optional `accentColor` field on the wire patch. An older
  host ignores the field, so a patch carrying only it applies nothing there and `ApplyAsync` returns
  `false`. While a Slider's or History Graph's colour thresholds are on, a stored accent has no visible
  effect.
- [Video streams](/features/video-streams/) added a capability kind, `video-stream-provider`, a host API,
  `video-streams`, and `video_stream_` error reasons under the existing `CAPABILITY_UNAVAILABLE` code, all
  within major `3`. An older host rejects the kind non-fatally, and a plugin that does not implement
  `IVideoStreamIntegration` never declares it. The surface was reshaped before any release: Macro Deck now
  relays the media itself, and signaling, the consumer context and the `webrtc` and `whep` transports are
  gone. A development build written against the earlier, unreleased shape must be updated.
- [Calendars](/features/calendars/) added a capability kind, `calendar`, at capability version `1`, with
  its own operations, DTOs and reply limits, all within major `3`. An older host rejects the kind
  non-fatally and the plugin's other capabilities keep working, and a plugin that does not implement
  `ICalendarProvider` never declares it. The host's calendar widget types and the `calendar` trigger
  provider are host-owned additions, not protocol changes.
- [Configuration entries](/features/setup-flows/#taking-part-in-storing-an-entry) added the
  `IConfigEntryLifecycle` interface, every member with a default, and the capability kind `config-entries`
  at capability version `1`, within major `3`. An older host rejects the kind non-fatally and stores entries
  as before, and a plugin that does not implement the interface never declares it.
- `IConfigEntryLifecycle` gained a `SynchronizeEntriesAsync` overload that takes the integration's own
  `IVariableApi` and whose default calls the existing overload, so an implementation written before it is
  synchronized exactly as before; its `CancellationToken` has no default, so a call such as
  `SynchronizeEntriesAsync(entries, default)` still binds to the existing overload. `ConfigEntryPreparation`
  gained `RejectedVariableNames` (empty by default) and `ConfigEntryPreparationResult` gained
  `RetryOnConflict` (`false` by default), carried as the optional `rejectedVariableNames` and
  `retryOnConflict` on `config-entries`/`prepare`. An older host never retries; an older plugin never asks
  to, and is refused on the first conflict as before.
- `ConfigFlowResult` gained `CompleteWithoutEntry()` and `StoresEntry` (`true` for every other result),
  carried as the optional `withoutEntry` on a `flow.submit` `Complete` result. An older host does not know
  the flag and stores an entry from the collected values under the integration's name, as for
  `Complete` without a title.
- [Email inboxes](/features/email-inboxes/) added the `IEmailInboxProvider` interface, a capability kind,
  `email-inbox`, at capability version `1`, with its own operations, DTOs and reply limits, all within major
  `3`. `state.update` gained a meaning for this kind only: a `localId` names an inbox whose content or status
  changed, and the host reads that inbox again without re-describing the kind; every other kind reads
  `localId` as before. An older host rejects the kind non-fatally and the plugin's other capabilities keep
  working, and a plugin that does not implement `IEmailInboxProvider` never declares it. The host's email
  widget, variables and `email` trigger provider are host-owned additions, not protocol changes.
- [Event origins](/ui/views/sessions/#answering-the-client-that-acted) added `IUiEventOrigin` and an
  `IUiSession.Dispatch(UiEvent, IUiEventOrigin)` overload whose default forwards to `Dispatch(UiEvent)`, so a
  session compiled before it keeps receiving every event. The `ui` host API gained `origin.report-failure`
  and `origin.show-modal`; against an older host they answer `false` or a cancellation.
- `IUiEventOrigin` gained `OpenUrlAsync` as a default interface member that checks the URL and answers
  `false`, so an origin implemented before it keeps compiling and loading. The `ui` host API gained
  `origin.open-url`; against an older host, and on a client that did not declare it can open links, it
  answers `false`. A client declares that ability when it connects, in a form an older host ignores.
- [Reporting values from an action's configuration](/ui/views/configuration/#reporting-values-the-tree-edits)
  added `IActionConfigurationSession`, `ActionConfigurationSession` and
  `ActionParameterValuesReportedEventArgs`. A configuration session written before them is unchanged and
  never reports. The `ui` host API gained `report-parameter-values`, which an older host answers with
  `CAPABILITY_UNSUPPORTED` and the SDK then drops, so a reported value is not saved there. The `ui`
  capability gained `session.flush`, which a plugin built before it answers with `CAPABILITY_UNSUPPORTED`
  and the host treats as flushed.
- `IVariableApi` gained `RequestRefreshAsync` and `NotifyDeclarationsChangedAsync`, both defaulting to a
  no-op, and the `variables` host API gained `refresh`; an older host makes the request a no-op.
  `NotifyDeclarationsChangedAsync` uses the existing `state.update`.
- `IVariableApi` gained `RequestDefinitionRefreshAsync`, defaulting to `RequestRefreshAsync`, and
  `variables`/`refresh` gained an optional `definitionIds` list; an older host ignores it and reads every
  declared variable, which is always allowed. `IVariableApi` also gained `BindAsync`, whose default throws
  `NotSupportedException`, and the `variables` host API gained `bind`; against an older host the SDK
  reports the bindings as not supported. Two limits were added: `maxVariableRefreshDefinitionIds` and
  `maxVariableBindingsPerCall`.
- `RequestDefinitionRefreshAsync` and `variables`/`refresh` with `definitionIds` also bring catalog resources
  a user bound forward, not only declared variables, when Macro Deck polls them; a pushed resource is
  unaffected. Reading more than asked for was always allowed. An older host ignores a catalog resource id,
  so that resource keeps its refresh interval.
  `MacroDeck.Plugin.Hosting` keeps the named ids when it merges a pending request with a request for every
  declared variable and sends them next to it, split into lists of at most `maxVariableRefreshDefinitionIds`;
  an earlier version folded them into the request for every declared variable.
- `VariableNames` (`MacroDeck.Sdk.Variables`), `HostOptionsSources` (`MacroDeck.Sdk.Actions`) and the
  `NativeKeys` members `IsPositional`, `TryGetWindowsScanCode`, `TryGetMacOsKeyCode` and
  `TryFromWindowsScanCode` are new SDK helpers. They need no host support: the option source ids are the
  values every Macro Deck already answers, and the name rules are the ones every Macro Deck already applies.
- `IIntegrationContext` gained `Storage`, `Locale` and `Http`, and `IConfigFlowContext` gained `Http`, each a
  default interface member: `UnavailableIntegrationStorage.Instance` (every key missing, a write throws
  `InvalidOperationException`), `LocalLocaleApi.Instance` (the current culture with a 24-hour clock) and
  `DefaultHostHttp.Instance` (`MacroDeck/` and the SDK version), so an existing implementation keeps
  compiling and behaves as before. The `locale` host API (`time-of-day-format`) and the push-only `http`
  `host.state` are new: an older host answers `CAPABILITY_UNSUPPORTED` or pushes nothing, and the SDK falls
  back to those same defaults; an older SDK ignores the `http` push. Storage needs no host support: a plugin
  keeps it in its own data directory. `HourCycles` and `TimeOfDayFormat` (`MacroDeck.Sdk.Locale`) and
  `IntegrationStorageKeys` (`MacroDeck.Sdk.Storage`) are new SDK types. After 3.0.0-beta.17,
  `TimeOfDayFormat.Format` writes a 24-hour clock in a culture whose pattern is 12-hour with a two-digit
  hour (`09:05`, before `9:05`); the API is unchanged, and a plugin built against beta.17 keeps its own copy
  of the formatter until it is rebuilt.
- `maxUiResourceBytesPerPlugin` and `maxUiResourcesPerPlugin` rose from 16 MiB and 256 to 48 MiB and 1024.
  The host advertises them in the protocol descriptor; a plugin built against an older SDK keeps its
  own, lower quota, which a newer host still accepts.
- `MacroDeck.Ui` added `UiWidgetSafeArea` and `UiWidgetSurface` in `MacroDeck.Ui.Widgets`.
- `IWidgetApi` gained `GetWidgetDataAsync`, defaulting to `null`, and the `widgets` host API gained
  `get-data`; an older host answers no data. `IVariableApi` gained `OpenHistoryAsync`, whose default throws a
  `VariableHistoryException` with `Unsupported`, and the `variables` host API gained `history.open`,
  `history.read` and `history.close`; against an older host the SDK reports the history as unsupported.
- `UiWidgetSurfaceAttributes` gained `culture`, with the reader `UiWidgetSurface.Culture`. A provider that
  ignores it, or reads a surface from an older host without it, draws its own text in its default language.
- While the host is locked, a client's events to a plugin's `widget` or `dialog` session are refused with
  `HOST_LOCKED`, except `reveal`. A plugin never received a guarantee that such events arrive while
  locked, and the hardware path already refused them.
- A Weather widget's default press now runs the details action its station's provider declares through
  `IWeatherProvider.DetailsAction`, instead of always running the built-in Weather integration's **Weather
  details** action. A weather plugin built against an older SDK declares none, so its stations get no default
  press; a flow the user added still runs. See
  [Pressing the widget](/features/weather/#pressing-the-widget).
- Weather providers gained `IIntegrationContext.WeatherProvider` (`IWeatherProviderApi`,
  `NotifySnapshotsChangedAsync`) and the `weather` host api with `snapshots-changed`, all within major `3`.
  The member is a default interface member returning `UnsupportedWeatherProviderApi.Instance`, so an
  existing implementation keeps compiling. An older host answers `CAPABILITY_UNSUPPORTED`, which the SDK
  turns into a no-op; the host then reads the stations on its own interval as before. See
  [Telling Macro Deck the weather changed](/features/weather/#telling-macro-deck-the-weather-changed).
- [Android devices](/features/android-devices/) added typed operations to `IAndroidDevice` (`SendKeyAsync`,
  `TapAsync`, `SwipeAsync`, `InputTextAsync`, `StartAppAsync`, `ForceStopAppAsync`, `OpenUriAsync`,
  `RebootAsync`, `CaptureScreenshotAsync`, `GetStatusAsync`), `IAndroidDevice.LacksUsbPermission`,
  `IAndroidDevice.Mode`, `IAndroidDeviceManager.DefaultDevice`, `IAndroidDeviceManager.IsAdbUnavailable`, the
  `AndroidKey`, `AndroidRebootMode`, `AndroidDeviceMode` and `AndroidDeviceStatus` types, ten `adb` host api
  operations, the asset kind `android-screenshot`, and the optional `defaultDeviceSerial`, `adbUnavailable`,
  `lacksUsbPermission` and `mode` fields of the `adb` state, all within major `3`. The members are default interface
  members, so an `IAndroidDevice` or `IAndroidDeviceManager` written against an older SDK keeps compiling and
  loading: its typed operations throw `AndroidDeviceException` with `Unsupported`, `GetStatusAsync` answers
  `null`, `LacksUsbPermission` and `IsAdbUnavailable` are `false`, `Mode` is `Normal` and `DefaultDevice` is
  `null`. An older host answers the new
  operations with `CAPABILITY_UNSUPPORTED`, which the SDK reports the same way, and an older plugin ignores
  the new state fields: a device lacking USB permission still reads `unauthorized`, one in recovery `offline`. `AndroidDeviceState` gains
  no value. The typed operations run while the host is locked, see
  [ADR 0092](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0092-plugins-reach-adb-through-a-permission-gated-host-api.md).
- [Companion devices](/features/companion-devices/) added `IIntegrationContext.CompanionDevices`, the
  `MacroDeck.Sdk.Companion` types (`ICompanionDeviceApi`, `UnsupportedCompanionDeviceApi` and the device,
  state, command, result and failure types), the `companion-devices` host API (`wait`, `send`, `request`,
  `screenshot`) with a per-plugin `host.state` push, the error code `COMPANION_DEVICES_NOT_ALLOWED`, the asset
  kind `companion-screenshot` and the permission `host:companion-devices`, all within major `3`.
  `CompanionDevices` is a default interface member returning `UnsupportedCompanionDeviceApi.Instance`, so an
  existing `IIntegrationContext` implementation keeps compiling and loading. An older host pushes nothing and
  answers `CAPABILITY_UNSUPPORTED`; the SDK then reports access `Unsupported`, answers a send with `false` and
  fails a request or screenshot with `NotConnected`, without throwing. An older plugin ignores the push, and
  an older host installs a manifest that declares `host:companion-devices` with the `unknown-permission`
  warning. The permission is enforced from the start, which breaks no plugin because none used the API
  before; see
  [ADR 0110](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0110-plugins-reach-companion-devices-through-a-permission-gated-host-api.md).
  Capability names, failures and network types are open strings: a reader ignores a capability it does not
  know and reads an unknown failure as `failed`.
- [Reading every provider's players](/features/music-players/#reading-every-providers-players) added
  `IIntegrationContext.MusicPlayers`, the `MacroDeck.Sdk.MusicPlayer` types `IMusicPlayerApi`,
  `MusicPlayerInstanceSnapshot` and `UnsupportedMusicPlayerApi`, and the `music-players` host API
  (`album-color`) with a `host.state` push sent to every plugin, all within major `3`. `MusicPlayers` is a
  default interface member returning `UnsupportedMusicPlayerApi.Instance`, so an existing `IIntegrationContext`
  implementation keeps compiling and loading and reports no players. An older host pushes nothing and answers
  `CAPABILITY_UNSUPPORTED`, which the SDK reports as no players and a `null` colour, without throwing; an
  older plugin ignores the push. Reading is not gated, like registering another player's artwork.
- [Desktop notifications](/features/notifications/#desktop-notifications) added
  `IIntegrationContext.DesktopNotifications`, the `MacroDeck.Sdk.Notifications` types `IDesktopNotifier` and
  `UnsupportedDesktopNotifier`, the `desktop-notifications` host API (`show`) with a per-plugin `host.state`
  push on registration, the error code `DESKTOP_NOTIFICATIONS_NOT_ALLOWED`, the permission
  `host:desktop-notifications`, and `FakeDesktopNotifier` in `MacroDeck.Plugin.Testing`, all within major `3`.
  `DesktopNotifications` is a default interface member returning `UnsupportedDesktopNotifier.Instance`, so an
  existing `IIntegrationContext` implementation keeps compiling and loading and shows nothing. An older host
  pushes nothing and answers `CAPABILITY_UNSUPPORTED`; the SDK then reports `IsSupported` as `false` and
  `ShowAsync` completes without showing anything, without throwing. An older plugin ignores the push, and an
  older host installs a manifest that declares `host:desktop-notifications` with the `unknown-permission`
  warning. The permission is enforced from the start, which breaks no plugin because none used the API before.
- Colour variables added `VariableType.Color`, whose values travel as an ordinary `text` value, and an
  optional `allowAlpha` field on `ActionParameterDto` (`ActionParameter.AllowAlpha`), all within major `3`.
- [Batteries](/features/batteries/) added a capability kind, `battery-provider`, at capability version
  `1`, a host API, `batteries`, with a `host.state` push sent to every plugin, and the
  `MacroDeck.Sdk.Batteries` types, all within major `3`. `IIntegrationContext.Batteries` is a default
  interface member returning `UnsupportedBatteryApi.Instance`, so an existing implementation keeps
  compiling. An older host answers a publish with `CAPABILITY_UNSUPPORTED`, which the SDK turns into a
  no-op, and an older plugin ignores the `batteries` state. Kinds, charge states and health values are open
  strings: a reader maps one it does not know to `other` or unknown.
  An older host drops a variable definition that declares `Color` and ignores `allowAlpha`. A colour
  parameter without `allowAlpha` still receives opaque `#rrggbb`, also when the user bound it to a
  variable. A new optional `hostApiFeatures` list on the session request lets a plugin declare
  `scripts.input-color`; only then does a Color script input reach it as `color`, every other plugin keeps
  receiving it as `text` with the hex value. The SDK declares it automatically. See
  [Color variables](/features/variables/#color-variables).
- Colour variables are also a one-way step for the host's own data, which no plugin contract covers: once
  a user variable of type `Color` exists, the persisted user-variables file cannot be read by a Macro Deck
  release from before `Color`, which then starts without any user variables. Downgrading the host is
  unsupported from that point; see [Colors from a variable](/guide/tips/#colors-from-a-variable).
- Script and automation variables added `ActionExecutionContext.ScriptId` and `AutomationId`, the
  `UserVariableContext` record, `IUserVariableApi.ApplyInContextAsync` and `CreateInContextAsync`, the
  `UserVariableCreateStatus` values `UnknownScript` (7) and `UnknownAutomation` (8), and optional
  `scriptId` and `automationId` fields on the `user-variables` `apply` and `create` arguments and on the
  `actions` `execute` arguments, all within major `3` and the `actions` capability version `1`. The new
  methods are default interface members that delegate to `ApplyAsync` and `CreateAsync` with the owner
  widget, so an existing `IUserVariableApi` implementation keeps compiling and loading, and calls of the old
  methods, `null` arguments included, resolve as before. An older host never sends the new `execute` fields
  and ignores them on `apply` and `create`, so a plugin gets widget and global behaviour there; an older
  plugin never sends them and gets the same on a new host. See [Scopes](/features/variables/#scopes).
- Script and automation variables are another one-way step for the host's own data: once a variable local
  to a script or an automation exists, the persisted user-variables file cannot be read by a Macro Deck
  release from before them, which then starts without any user variables. Downgrading the host is
  unsupported from that point, as with `Color`.
- Resolving colours added the `colors` host api (`resolve`, `watches` and a `colors` `host.state`), the
  optional `maxColorWatches` field in the protocol descriptor's limits, and `IIntegrationContext.Colors` as
  a default interface member, so an existing `IIntegrationContext` implementation keeps compiling and
  loading. An older host answers `CAPABILITY_UNSUPPORTED`, and the SDK falls back to resolving fixed colours
  locally, giving `null` for a reference and delivering a watch once. See
  [Resolving colours yourself](/features/variables/#resolving-colours-yourself).
- Searchable selects added `ActionParameter.Searchable` with `ActionParameter.AsSearchable()`, and an
  optional `searchable` field on `ActionParameterDto`, all within major `3`. An older host ignores it and
  draws the plain select; a parameter from an older plugin has no `searchable` and is not searchable. See
  [Letting the user search a long list](/features/actions/#letting-the-user-search-a-long-list).
- Action roles added `IActionDefinition.Role`, a default interface member returning `null`, the
  `ActionRoles` constants and an optional `role` field on `ActionDescriptorDto`, all within major `3` and
  `actions` capability version `1`. An existing `IActionDefinition` keeps compiling and loading with no
  role; an older host ignores the field. `ActionRoles.RunScript` and `ActionRoles.RunRemoteScript` were
  added the same way; an older host's editor offers script inputs only for its own built-in script actions. See [Declaring parameters](/features/actions/#declaring-parameters).
- [Companion device entries](/features/companion-devices/#a-configuration-entry-per-device) added the
  member-less `ICompanionDeviceConfigEntries` interface and a declarative capability kind,
  `companion-device-entries`, at capability version `1` with the single operation `describe`, answered
  without data, all within major `3`. A plugin that does not implement the interface never declares the kind
  and is unaffected; an older host rejects the declaration non-fatally and creates no entries for the plugin.
  The built-in Companion integration now reaches the same behaviour through the interface instead of being
  named by host code, so its entries are created and removed exactly as before.
- Shareability added `VariableDefinition.IsShareable` (default `true`) and an optional `isShareable` field
  on `VariableDefinitionDto`, defaulting to `true` when absent, all within major `3` and without moving the
  `variables` capability version: a definition from an older plugin stays shareable, and an older host
  ignores the field. See [Defining a variable](/features/variables/#defining-a-variable).
- [Code parameters](/features/actions/#code-parameters) added `ActionParameter.VariableInsertFormat` and an
  optional `variableInsertFormat` field on `ActionParameterDto`, all within major `3`, and the documented
  `language` vocabulary gained `autohotkey`. A parameter from an older plugin has no format and its editor
  offers no variable insertion; an older desktop app ignores the field. A language an editor does not know
  is edited as plain text, never refused.
- [Rendering array items](/features/actions/#rendering-array-items) added `ActionParameter.TemplatedItems`
  with `ActionParameter.WithTemplatedItems()`, and an optional `templatedItems` field on `ActionParameterDto`,
  all within major `3`. Absent or `false` keeps the items unrendered, exactly as before, so an older
  plugin's `Array` parameters are unchanged; an older host ignores the field and passes the items as
  entered.
- [Reading a variable as a flow sees it](/features/variables/#reading-a-variable-as-a-flow-sees-it) added
  `IUserVariableApi.ReadInContextAsync`, a default interface member answering `NotSupported`, the
  `UserVariableReadStatus` enum and the `UserVariableReadResult` record, and the `user-variables` `read`
  operation, all within major `3`. An existing `IUserVariableApi` implementation keeps compiling and
  loading. An older host answers `read` with `CAPABILITY_UNSUPPORTED`, which the SDK reports as
  `NotSupported`; a transport failure or an empty reply is reported as `Unavailable`.
- [Integration issues](/features/integration-issues/#what-an-issue-carries) added `IntegrationIssue.HelpUrl`
  and an optional `helpUrl` field on a listed issue, all within major `3`. Only an absolute `http` or `https`
  address is shown. An issue from an older plugin has no link, and an older host ignores the field.
- [Integration pages](/ui/views/page/) added `UiSurfaceKinds.Page`, `UiPageSurfaceAttributes`,
  `UiPageDeclaration` and `IUiProvider.Pages`, a default interface member answering no pages, and an
  optional `pages` member on `ui` `describe`, all within major `3` and `ui` capability version `1`. A
  provider written before pages keeps compiling and declares none; an older host ignores the member and the
  `page` surface and shows no tab. Conformance check [MDC0316](/reference/conformance/#mdc0316) was added
  for pages declared without a `page` surface or with a malformed or repeated id.
- [Uploaded code input text](/ui/views/configuration/#text-too-large-for-the-tree) added
  `IUiEventOrigin.ReadUploadAsync`, a default interface member throwing `NotSupportedException`,
  `UiUploadNotFoundException`, the `ui` host API operation `origin.read-upload` and the host-to-plugin
  asset kind `ui-upload`, all within major `3`. An origin implemented before it keeps compiling and
  loading. An older host answers `CAPABILITY_UNSUPPORTED`, which the SDK turns into
  `NotSupportedException`. See [the protocol reference](/reference/protocol/#ui-uploads).
- [UI resources](/ui/reference/resources/) accept `text/plain`, served as UTF-8, for the text of a code
  input. Nothing changed for the image types. An older host refuses a text registration, which throws
  `UiResourceException` with `Failed`.
- Localization moved the **UI model** major, not this one - see [the localization major](#the-localization-major).

**Negotiation happens exactly once**, in `POST /api/plugins/sessions`:

```text
plugin sends:  { minimum: clientMin, maximum: clientMax }
host computes: negotiated = min(clientMax, current)
fails with PROTOCOL_VERSION_UNSUPPORTED when negotiated < max(clientMin, minimum),
echoing the host's own supported range
```

The later `session.hello` only *asserts* the outcome - it never re-negotiates.

**A protocol break means bumping the major and keeping the previous version served for the negotiated
range.** Version `1` is never silently redefined. A plugin declaring `{ minimum: 1, maximum: 1 }` in its
manifest's `compatibility.protocol` keeps negotiating version 1 against a host that has moved on, until 1
leaves that host's supported range - at which point the failure is explicit and diagnosable.

**Capabilities negotiate independently**, per kind, on the same min/max algorithm but with a
**non-fatal** failure policy: an unknown kind, or one whose range does not overlap the host's, is
rejected with a reason and the session proceeds degraded. An unknown host API call is answered with
`CapabilityUnsupported` rather than failing the session, and a plugin that never declares a kind is never
asked about it.

### UI model majors

#### The localization major

Localizable Macro Deck UI text properties moved **`MacroDeck.Ui.Model`** (`UiModelVersions.Current`)
from `1` to `2`: a text-bearing configuration property may now carry a localization reference where it
always carried a JSON string, so the session has to say which version it speaks.

**Localization did not move the plugin protocol's major.** The protocol moved to `2` for an unrelated
reason (widget state by stable id) and localization would have left it at `1`. The two majors have never
been coupled (see
[ADR 0038](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0038-ui-model-and-declarative-dsl.md)).
Localization broke neither wire: the `localization` capability kind and its limits are additive, and a
localized value reads a plain JSON string as literal text.

**Version `1` of the UI model stays served**: the host opens every session at the current version and
honours whatever a provider negotiates down to, so a plugin on version `1` keeps emitting plain strings
untouched. See
[ADR 0057](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0057-localization-is-a-deferred-reader-resolved-reference.md).

#### The component major

Renaming the component vocabulary moved **`MacroDeck.Ui.Model`** (`UiModelVersions.Current`) from `2` to
`3`. Every node type was respelled: the ten a reader can draw from the tree alone became `ui.*`, the four
that resolve a Macro Deck reference became `macrodeck.*`.

```text
widget.*  ->  ui.*          (drawn from the tree alone)
widget.*  ->  macrodeck.*   (resolves a Macro Deck reference)
```

**This one also moved `UiModelVersions.Minimum`.** It is a hard cut - no `widget.*` spelling is
accepted, and no shim is published - so **majors `1` and `2` of the UI model are no longer served.**
Advertising them would be worse than refusing them: an unknown node type is non-fatal, so a version `2`
tree would negotiate and then render every node as the unsupported placeholder. This was affordable
exactly once, because no plugin served a component surface and a UI tree is built per session and never
persisted. See
[ADR 0064](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0064-components-are-a-registry-over-two-namespaces.md).

#### What did not need a UI model major

New component types and properties are additive. The [modifiers](/ui/components/modifier/) added a
`modifiers` property an older reader ignores, five gesture event names it never sends, and the `ui.modifier`
type it answers with the node's `fallback`; `UiModelVersions` did not move. [Responsive layouts](/ui/components/responsive/)
added the `ui.responsive` type the same way, and [first-fit layouts](/ui/components/first-fit/) the `ui.first-fit` type. See
[UI model compatibility](/ui/reference/compatibility/#additions-that-moved-no-version).

The [code input](/ui/views/configuration/#code-input) added the open `UiCodeLanguages` vocabulary and the
`variableInsertFormat` and `contentResource` properties (`UiCodeInput.VariableInsertFormat` and
`ContentResource`), the `content-uploaded` and `content-upload-failed` events with
`UiCodeInput.OnContentUploaded`, `OnContentUploadFailed` and `UiContentUpload`, and the `page` surface kind
with `UiPageSurfaceAttributes`; `UiModelVersions` did not move. An older client ignores both properties,
offers no variable insertion and edits the bound value, and never sends the new events; it has no page
surface to open.

The code input later added `rows` (`UiCodeInput.Rows`), the editor's visible line count; an older reader
ignores it and draws its default height.

The [page layout](/ui/views/configuration/#page-layout) added the configuration types `split` (`UiSplit`),
`list` (`UiConfigList`), `list-item` (`UiConfigListItem`) and `empty-state` (`UiEmptyState`), the properties
`railTitle`, `selected`, `badge`, `iconWithLabel`, `align` and `gap`, `variant` on `button` and `stack`, and
the value vocabularies `UiButtonVariants`, `UiStackAlignments`, `UiStackGaps` and `UiStackVariants`;
`UiModelVersions` did not move. An older reader declines the new types and draws each node's explicit
fallback, and ignores the new properties, drawing an ordinary button, a plain stack and a heading without a
badge.

### What "maintained per major" means in practice

- **Within an SDK major**, nothing frozen is removed or changed incompatibly. A minor or patch may add,
  and may deprecate; it may not remove. Upgrading the SDK inside a major should never require a source
  change to your plugin.
- **At an SDK major**, only APIs whose `[MacroDeckDeprecated]` named *that* major as their `RemovedIn`
  disappear. You will have had at least one release cycle of compile-time and run-time warnings naming
  the replacement.
- **Within a protocol major**, the error-code list and the message-type catalogue are append-only.
- **At a protocol major**, the previous major stays negotiable for as long as the host's supported range
  says it does. Read the descriptor at `GET /api/plugins/protocol`, which needs no authentication, rather
  than hard-coding a version.
- **Limits and timeouts are advertised, not fixed.** They are published in the protocol descriptor and
  again in the session response. A value changing is not a breaking change; read them at run time.

## Verifying compatibility yourself

- Run the **conformance suite** against your plugin: `macrodeck-plugin test`. A broken check id is a
  break, and the ids are stable, so gate CI on them. See [conformance](/reference/conformance/).
- Reference **`MacroDeck.Plugin.Analyzers`**. Its source generator records the deprecated APIs your
  compilation actually references, so the host can report *confirmed* rather than *inferred* usage. A
  plugin without it lands in `inferred`, which is never presented to a user as fact.
- Check the **compatibility report** in the session response: an overall state, the evidence source, and
  per-finding guidance. States, best to worst: `compatible`, `deprecated_apis`, `update_recommended`,
  `update_required`, `partially_incompatible`, `incompatible`.

## If a fix cannot be made without a break

It is not made. A change that breaks a non-obsolete public contract is not shipped as a bug fix, is not
made unilaterally, and does not arrive in a minor release. It goes through deprecation, or through a
protocol major, or it does not happen.

## See also

- [Deprecations](/policies/deprecations/) - the lifecycle, the registry, and the evidence model.
- [Migrations](/policies/migrations/) - what a migration guide contains, and when one is published.
- [Conformance](/reference/conformance/) - the contract suite and its stable check ids.
- [Plugin protocol](/reference/protocol/) - version and capability negotiation in detail.
