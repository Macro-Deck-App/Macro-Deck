# Companion version compatibility and wire contract

The Companion app (Macro-Deck-App/Macro-Deck-Companion) and the host each name the oldest version of the other
they work with. The app reads the host's side on the anonymous `GET /api/auth/status` and decides; the host
never refuses an app for its version. Both fields are pinned by
[AuthPolicyMatrixTests](../../host/tests/MacroDeckHost.Tests.UnitTests/Auth/AuthPolicyMatrixTests.cs) and
[LockedHostGateTests](../../host/tests/MacroDeckHost.Tests.UnitTests/Api/LockedHostGateTests.cs).

## The fields

`GET /api/auth/status` adds, next to the fields the [Macro Deck 2 app](macro-deck-2-app.md) relies on:

- `version`: `HostVersion.Current`, the host's version without build metadata, for example `3.0.0-beta.15`.
- `minimumCompanionVersion`: `CompanionCompatibility.MinimumCompanionVersion`, the oldest Companion release
  this host works with, for example `26.2.0`.

Both are answered anonymously and while the key ring is locked, because an app too old may not get far enough to
sign in. An older app ignores them. The version was already public on the LAN through the mDNS TXT record; this
also names it to anyone who reaches the public listener, for example through a port forward.

## Ordering

Versions compare by Semantic Versioning 2.0 precedence: `3.1.0 > 3.1.0-beta.1 > 3.0.0 > 3.0.0-beta.13 >
3.0.0-beta.12`, with numeric pre-release identifiers compared as numbers and build metadata ignored. The app
treats a missing or unreadable value, and the fallback `0.0.0-dev`, as unknown, which refuses nothing.

## Raising the minimum

Raise `MinimumCompanionVersion` in the host release that stops working with older apps, and only to a Companion
version that is already released on every store. The app's own minimum host version lives in the Companion
repository, `MINIMUM_HOST_VERSION`, and its contract is `docs/integration/macro-deck-host.md` there.

## Wire contract

An installed Companion keeps talking to every newer host, so the parts of the pairing API and of the UI socket
(`/ws/ui`, [ADR 0062](../decisions/0062-ui-realtime-transport.md)) it uses are frozen here, as the host accepts
and answers them. The vectors are the frames the Companion sends, taken from its sources (`network/UiSocket.kt`,
`network/HostWire.kt`, `network/HostApi.kt`, `deck/DeckClient.kt`, `deck/DeckModels.kt`, `deck/WidgetSessions.kt`,
`deck/SessionRecovery.kt`, `deck/CompanionReporter.kt`, `license/LicenseSync.kt`,
`notifications/NotificationFeed.kt`, `video/VideoStreamClient.kt`, `video/VideoStreamWire.kt`). How the app times
out, retries and recovers is its own contract, `docs/integration/macro-deck-host.md` in the Companion repository.
Every vector is replayed against a real host by the `CompanionWireCompatibilityTests` system tests
([pairing, socket and presses](../../host/tests/MacroDeckHost.Tests.SystemTests/CompanionWireCompatibilityTests.cs),
[deck and sessions](../../host/tests/MacroDeckHost.Tests.SystemTests/CompanionWireCompatibilityTests.Deck.cs),
[device, host state and REST](../../host/tests/MacroDeckHost.Tests.SystemTests/CompanionWireCompatibilityTests.Device.cs)),
except where a section says otherwise.

The Companion decodes the deck's widgets, folders and profiles strictly: a missing required field, a field of the
wrong type or an explicit `null` in a field it does not allow to be null drops the whole message, and in
`GET /api/folders` the whole read. The fields it needs are named below. Every other field is optional to it, and
a field the host leaves out because it is null is read as missing.

Changes are additive only: a new optional field, request or push is fine. Renaming or removing a field, changing
its type, an answer's shape, a code string or a push's type name needs a Companion release that no longer reads
it first, and then `MinimumCompanionVersion` raised past the releases that still do.

### Pairing redeem

`POST /api/auth/device-enrollment/redeem` on the public listener, anonymous
([AuthController](../../host/src/MacroDeckHost/Api/Controllers/AuthController.cs)):

```json
{"token":"<pairing code>","device":{"clientType":"native","proposedName":"Compat Phone","platform":"Android 14",
 "formFactor":"phone","appVersion":"26.2.0"}}
```

- Every `device` field is nullable and the block is optional; without it the device is registered as unknown.
  The Companion leaves a null field out, so a first pairing sends no `deviceId`, `deviceSecret` or `browser`.
- `clientType` matches exactly `web-client`, `admin-ui` or `native`; anything else is stored as `unknown`. The
  Companion sends `native`.
- `formFactor` matches exactly `desktop`, `tablet` or `phone`; anything else is stored as `unknown`.
- `deviceId` and `deviceSecret` are what the app sends back on a later login to keep the same device row.

The answer is `200` with the login answer the Companion decodes:

```json
{"accessToken":"<jwt>","expiresInSeconds":5184000,"scope":"client","username":"owner",
 "device":{"deviceId":"<guid>","deviceSecret":"<hex>","startupProfileId":null},"hostKey":"<base64>"}
```

`deviceSecret` is only on the answer that minted the device, `hostKey` is left out when the host has none, and the
cookies are set as for a login.

### The socket

`POST /api/ui-websocket/tickets` with `{}`, the header `X-MacroDeck-Ui-Protocol: 1` and the client-scope bearer
token answers `{"value":"<ticket>"}`. The socket opens at `/ws/ui?ticket=<ticket>` with the subprotocol
`macrodeck.ui.v1` ([UiWebSocketEndpoint](../../host/src/MacroDeckHost/Ui/UiWebSocketEndpoint.cs)).

Every frame is one envelope `{protocolVersion, kind, type, id, correlationId, payload, error}`. The host writes
camelCase and leaves out null fields, and every frame it sends carries `"protocolVersion":1`: the Companion drops a
frame with any other version, a welcome included.

| Frame | Rule |
| --- | --- |
| `{"protocolVersion":1,"kind":"hello"}` | Must be the first frame; anything else closes the socket with 1008. |
| `{"protocolVersion":1,"kind":"welcome"}` | The host's answer. It carries the hello's `id` as `correlationId`, so a hello without an id gets none. |
| `{"protocolVersion":1,"kind":"ping","id":"ping-1"}` | Answered with `{"protocolVersion":1,"kind":"pong","correlationId":"ping-1"}`. |
| `{"protocolVersion":1,"kind":"pong","correlationId":null}` | The Companion's answer to the host's ping, which the host sends every 20 seconds without an id. Accepted. |
| `{"protocolVersion":1,"kind":"request","type":"<type>","id":"<id>","payload":[...]}` | Answered by a `response` with `type`, `correlationId` set to `id` and `payload`, or by an `error` with `type`, `correlationId` and `error.code`. |

The payload is positional: argument `n` is element `n` of the array. A request type the host does not know answers
`{"protocolVersion":1,"kind":"error","type":"<type>","correlationId":"<id>","error":{"code":"unknown_type"}}`,
which the Companion reads as an older host. A request the token's scope may not make answers `forbidden`. The
Companion reads only `error.code`.

A push is `{"protocolVersion":1,"kind":"message","type":"<EventName>","payload":{...}}`, with the event's name as
the type.

### RegisterClient

```json
{"protocolVersion":1,"kind":"request","type":"RegisterClient","id":"1-register","payload":["compat-client-1"]}
```

The id names this installation's deck for actions that aim at "this client". A second argument, an array of
feature names, is optional and the Companion does not send it. Before the answer, which has a `null` payload, the
host pushes `ActiveNotificationsEvent` `{"notifications":[...]}`.

### Deck

The deck follows these pushes, which reach every UI socket:

- `ProfileCreatedEvent` `{"profile":{"id":"<string>",...}}`
- `FolderCreatedEvent` `{"folder":{"id":"<string>","profileId":"<string>",...}}`
- `WidgetCreatedEvent` `{"folderId":"<string>","widget":{"id":"<string>","type":"ActionButton","positionX":0,
  "positionY":0,"width":1,"height":1,"data":"<json text>"}}`. `folderId` is required, the geometry is integers,
  and `data` is a JSON string or absent, never an object: the Companion decodes the widget strictly and drops one
  that does not fit.

The client reports where it is and opens one UI session per widget on screen:

| Request and payload | Answer |
| --- | --- |
| `ReportFolderChanged` `[{"folderId":"<id>","clientId":"compat-client-1","isResync":false}]` | `{"success":true}`; `navigationToken` is added only while a navigation the host pushed for this folder is pending |
| `OpenWidgetUiSession` `[{"widgetId":"<id>"}]` | `{"accepted":true,"sessionId":"<id>"}`, with `code` and `message` on a refusal |
| `AttachUiSession` `[{"sessionId":"<id>"}]` | `{"accepted":true,"sessionId":"<id>","revision":<int>,...}`, followed by `UiSessionTreeUpdatedEvent` |
| `CloseUiSession` `["<sessionId>"]` | `true`, or `false` for a session that is unknown or not this client's |

The session pushes:

- `UiSessionTreeUpdatedEvent` `{"sessionId","revision","tree"}`: a whole tree, which replaces the one drawn.
- `UiSessionPatchedEvent` `{"sessionId","fromRevision","toRevision","patch"}`: a change to the tree, such as the
  widget's data changing.
- `UiSessionClosedEvent` `{"sessionId","reason"}`: the session ended. A deleted widget's session ends with the
  reason `The widget was deleted.`. It is a diagnostic text, but the Companion compares it to tell that end apart,
  so it is frozen too.
- `UiSessionInvalidatedEvent`
  `{"sessionId":"<id>","code":"WIDGET_RECONFIGURED","message":"<text>","retryable":true}`: the session's view is
  gone. It reaches only the connections attached to that session. The Companion needs `sessionId`, drops the tree
  and resyncs its sessions; `retryable` tells a folder view it may open again, and `code` is passed on for a
  dialog's session. Replayed by changing the folder's corner radius under an open widget session.

### Presses

A widget whose tree declares the press answers it through its session:

```json
{"protocolVersion":1,"kind":"request","type":"SendUiEvent","id":"6-press",
 "payload":[{"sessionId":"<id>","nodeId":"actionButton","name":"press","revision":<int>}]}
```

`data` and `revision` are optional. The answer is `{"accepted":true}`, or `accepted: false` with a `code`. Every
other widget is pressed over REST with the client token, `POST /api/actions/execute`
([ActionsController](../../host/src/MacroDeckHost/Api/Controllers/ActionsController.cs)):

```json
{"widgetId":"<id>","folderId":"<id>","triggerType":"onShortPress","clientId":"compat-client-1"}
```

`triggerType` is `onTouchStart`, `onShortPress`, `onLongPress`, `onDoublePress` or `onTouchEnd`. The answer is
`{"success":true,"status":"Succeeded","executionId":"<id>","durationMs":0,"actions":[]}`, with
`error` `{code, message}` on a failure and `message` a LocalizedText. `status` is one of the strings `Accepted`,
`Succeeded`, `PartiallyFailed`, `Failed` and `Cancelled`; the Companion reports a run as failed by these names.

The refusal codes the Companion branches on are frozen as strings
([UiSessionErrorCodes](../../host/src/MacroDeckHost.Application/Ui/Sessions/UiSessionErrorCodes.cs)):
`SESSION_CLOSED`, `SESSION_FORBIDDEN`, `SESSION_NOT_FOUND`, `SESSION_BUSY`, `TOO_MANY_SESSIONS` and `HOST_LOCKED`.
A press on a closed session answers `SESSION_CLOSED` or `SESSION_NOT_FOUND`.

### ReportCompanionState

```json
{"protocolVersion":1,"kind":"request","type":"ReportCompanionState","id":"2-report","payload":[{
 "batteryLevelPercent":80,"charging":false,"orientation":null,"screenBrightnessPercent":50,
 "model":"Google Pixel 8","platform":"Android 14","appVersion":"26.2.0",
 "capabilities":["screenOn","screenshotDeck"],"requestableCapabilities":[],"inFocus":true,
 "networkType":"wifi","networkMetered":false,"networkValidated":null,"networkName":null,
 "cpuUsagePercent":null,"memoryUsedPercent":12}]}
```

The Companion writes every field, unknown ones as an explicit `null`; `charging` is never null. The fields after
`appVersion` are optional for older apps. The device is taken from the token, never from the payload, and a token
without a device answers `forbidden`. Unknown values are dropped and numbers and text are bounded (`Sanitize` in
[CompanionDeviceRegistry](../../host/src/MacroDeckHost/Integrations/CompanionDeviceRegistry.cs)). The answer is
`null`. The first report of a connection is followed by `CompanionWakeOnLanEvent`
`{"instanceName":"<string>","macAddresses":["..."]}`.

### Deck changes

These pushes reach every UI socket. A widget in them is the widget of `WidgetCreatedEvent` above: the Companion
needs `id` and `type`, reads the geometry as integers, `data` as a JSON string, `isPinned` as a boolean and
`pinScope` as a string, and replaces its stored widget with the one pushed.

| Push | Payload |
| --- | --- |
| `WidgetsCreatedEvent` | `{"folderId":"<id>","widgets":[<widget>,...]}` |
| `WidgetUpdatedEvent` | `{"folderId":"<id>","widget":<widget>}` |
| `WidgetsUpdatedEvent` | `{"folderId":"<id>","widgets":[<widget>,...]}`, for example after a batch pin |
| `WidgetPositionsUpdatedEvent` | `{"folderId":"<id>","widgets":[<widget>,...]}`, whole widgets, never positions alone |
| `WidgetDeletedEvent` | `{"widgetId":"<id>","folderId":"<id>"}` |
| `WidgetsDeletedEvent` | `{"folderId":"<id>","widgetIds":["<id>",...]}` |
| `FolderUpdatedEvent` | `{"folder":<folder>}` |
| `FolderDeletedEvent` | `{"folderId":"<id>"}` |
| `ProfileUpdatedEvent` | `{"profile":<profile>}` |
| `ProfileDeletedEvent` | `{"profileId":"<id>"}` |

A folder is the folder of `FolderCreatedEvent`, for example
`{"id":"<id>","name":"Home","profileId":"<id>","order":0,"isDefault":true,"viewId":"macrodeck.widget-grid","widgets":[]}`.
The Companion needs `id`, reads `name`, `profileId`, `parentId`, `backgroundColor`, `emptyCellStyle`, `viewId` and
`viewConfiguration` as strings, `order`, `rows`, `columns`, `widgetSpacing` and `widgetBorderRadius` as integers,
`isDefault` as a boolean and `widgets` as an array of widgets, one malformed widget dropping the folder.

A profile is the profile of `ProfileCreatedEvent`, for example
`{"id":"<id>","name":"Deck","order":1,"defaultRows":2,"defaultColumns":3}`. The Companion needs `id`, reads `name` and
`defaultEmptyCellStyle` as strings, `order`, `defaultColumns`, `defaultRows`, `defaultWidgetSpacing` and
`defaultWidgetBorderRadius` as integers and `defaultWidgetShadows` as a boolean.

### Navigation

`FolderNavigationEvent` moves the deck:

```json
{"protocolVersion":1,"kind":"message","type":"FolderNavigationEvent","payload":{"command":"back"}}
```

`command` is `back`, `parent` or `changeTo`. A `changeTo` carries `folderId`, `profileId` and, while the host waits
for the client to arrive, `navigationToken`, which the Companion sends back in its next `ReportFolderChanged` for
that folder. A navigation an action runs without a client reaches every socket; one aimed at a device, such as
`POST /api/devices/{id}/open-profile`, reaches only that device's sockets.

### State and runs

- `WidgetStateInvalidatedEvent` `{"widgetId":"<id>"}` reaches every socket when a widget's state changes from a
  state the host already reconciled, or its set of states changes. The Companion forgets the tree it remembers for
  that widget.
- `ActionExecutionStatusEvent` reaches the client that started a run which outlasted its answer, by the id from
  `RegisterClient`:
  `{"executionId":"<id>","status":"Succeeded","durationMs":6010,"widgetId":"<id>","triggerType":"onShortPress","actions":[]}`.
  `status` is one of the run statuses under [Presses](#presses). For `Failed`, `PartiallyFailed` and `Cancelled` the
  Companion shows `error.message`, a LocalizedText, when `error` is present.

### Dialogs

A dialog opened for the client that pressed arrives as `UiModalOpenedEvent`
`{"modalId":"<id>","title":{"$localized":{"scope":"macrodeck.app","key":"Widgets.Countdown.Dialog.Title"}}}`. The
Companion needs `modalId`; `title` is a LocalizedText and may be missing.

| Request and payload | Answer |
| --- | --- |
| `OpenModalUiSession` `[{"modalId":"<id>"}]` | `{"accepted":true,"sessionId":"<id>"}`, or `accepted: false` with `code`; then `AttachUiSession` as for a widget |
| `CompleteUiModal` `[{"modalId":"<id>","cancelled":true}]` | `{"accepted":true}`; a value the dialog returns goes in `value` with `cancelled: false`. The Companion ignores the answer and sends no `CloseUiSession` |

Replayed with a countdown that asks for its duration.

### Folder views

`OpenFolderUiSession` `[{"folderId":"<id>"}]` opens the session of a folder drawn by a folder view. For the widget
grid, which the client draws itself, it answers
`{"accepted":false,"sessionId":"","code":"INVALID_PAYLOAD","message":"<text>","viewId":"macrodeck.widget-grid"}`. The
Companion reads `accepted`, `sessionId`, `viewId` as a string and `code`, retrying on `TOO_MANY_SESSIONS`. An accepted
answer adds the session id and is followed by `AttachUiSession`; it is not replayed, see below.

### Screensaver

| Frame | Shape |
| --- | --- |
| `GetDeviceScreenSaver` `[]` | `{"enabled":false,"idleSeconds":300}`, read on every connect |
| `DeviceScreenSaverChangedEvent` | `{"enabled":true,"idleSeconds":60}`, to the device's sockets |
| `ShowDeviceScreenSaverEvent` | `{}`, to the device's sockets; the Companion shows the screensaver now |
| `OpenScreenSaverUiSession` `[]` | `{"accepted":true,"sessionId":"<id>","screenSaverId":"app.macro-deck.screensavers::clock","interactive":false}`, then `AttachUiSession`; `TOO_MANY_SESSIONS` is retried |

### Companion commands

`CompanionCommandEvent` reaches a device that reported its state:

```json
{"protocolVersion":1,"kind":"message","type":"CompanionCommandEvent","payload":{"command":"vibrate"}}
```

`command` is `setBrightness` (`brightnessPercent`), `setOrientation` (`orientation`: `automatic`, `portrait` or
`landscape`), `vibrate`, `screenOn`, `screenOff`, `focus`, `screenshot` (`requestId`, `screenshotMode` `deck` or
`full`) or `cancelRequest` (`requestId`); `requestId` is optional on the others. The Companion drops an unknown
command or one with a missing field. Replayed with `vibrate`.

### License

```json
{"protocolVersion":1,"kind":"request","type":"SyncCompanionLicense","id":"3-license","payload":[
 {"license":null,"proof":null,"trialDeviceId":"compat-trial-1","trialStarted":false}]}
```

The Companion writes all four keys. `proof` is `{"platform","productId",...}` with the store's optional receipt
fields. The answer is `{"license":null,"trialStartedAt":null,"revokedLicenseIds":[]}`. Unlike every other answer it
writes `license` and `trialStartedAt` even when they are null, and the Companion reads a null like a missing value.
A token without a device answers `forbidden`, and `unknown_type` tells the Companion the host keeps no trials.

`CompanionLicenseEvent` `{"license":"<token>"}` and `CompanionLicenseRevokedEvent` `{"licenseId":"<id>"}` reach the
Companions that reported their state; a license issued for a purchase proof also reaches the connection that sent
the proof. Only an answer from the Macro Deck Platform sends them, so they are not
replayed end to end: their frames are pinned by
[CompanionLicensePushFrameTests](../../host/tests/MacroDeckHost.Tests.UnitTests/Ui/CompanionLicensePushFrameTests.cs).

### Notifications

`RespondToNotification` `[{"id":"<id>","actionId":"<action>","values":{...}}]` answers a notification;
`"actionId":null` without `values` dismisses it. The answer is `{"status":"<status>"}` with `accepted`,
`already-closed`, `forbidden`, `invalid` or `not-found`; the Companion removes the notification on
`already-closed` and `not-found` and marks the dialog's input rejected on `invalid`. Replayed with an unknown id.

### Video streams

| Request and payload | Answer |
| --- | --- |
| `GetVideoStreams` `[{}]` | `{"providers":[{"id":"<id>","streams":[{"id":"<id>","name":<LocalizedText>,"width":1920,"height":1080,"state":"connected"}]}]}`, `{"providers":[]}` without a provider; the Companion treats `unavailable` and `disconnected` as down |
| `OpenVideoStream` `[{"providerId":"<id>","streamId":"<id>","acceptedTransports":["hls","mjpeg"]}]` | `{"sessionId":"<id>","revision":0,"state":"opening"}`, or an error with `unknown_provider`, `unknown_stream` or `transport_not_accepted` |
| `KeepAliveVideoStream`, `SuspendVideoStream`, `ResumeVideoStream` `[{"sessionId":"<id>"}]` | a response, or the error `unknown_session` |
| `CloseVideoStream` `[{"sessionId":"<id>"}]` | a response, also for an unknown session |

Error codes are `error.code` strings. No built-in provider exists, so the replay covers the empty catalog and the
refusals.

### Host language, appearance and lock

| Push | Payload |
| --- | --- |
| `LocalizationCultureChangedEvent` | `{"culture":"de","fallbackCulture":"en"}`; the Companion reads no field and reloads `GET /api/localization` |
| `LocalizationCatalogChangedEvent` | `{"scope":"plugin:<plugin id>"}`; likewise |
| `AppearanceChangedEvent` | `{"themeMode":"dark","accentColor":"#2196F3","fontFamily":""}`, each a string, each optional |
| `HostLockStateChangedEvent` | `{"locked":false,"lockScreenEnabled":true,"supported":true}`, three booleans |

### REST reads

The Companion reads these with its client token on the public listener and accepts only a `200`:

| Request | Fields it reads |
| --- | --- |
| `GET /api/folders?profileId=<id>` | `folders`, an array of folders as above |
| `GET /api/profiles` | `profiles`, an array of profiles as above |
| `GET /api/localization` | `culture` and `fallbackCulture` strings, `translations` with only string values, `hourCycle` string, `hourPadded` boolean |
| `GET /api/settings/appearance` | `themeMode`, `accentColor` and `fontFamily` strings |
| `GET /api/system/lock-state` | `locked`, `lockScreenEnabled` and `supported` booleans |
| `GET /api/system/time` | `utcMs`, a number |
| `GET /api/system/fonts` | `faces`, each with `faceId`, `family`, `slant` strings, `weight` integer, `remoteRenderable` and `userImported` booleans |
| `GET /api/system/fonts/{faceId}/file` | the font's bytes with an `ETag`; the Companion sends `If-None-Match` and accepts `304` |
| `GET /api/ui/resources/{resourceId}?v=<hash>&size=<int>&colorScheme=<scheme>&motion=<motion>` | the bytes, every query parameter optional; `404` for an unknown id. Replayed with `v` and `size`, not with `colorScheme` and `motion` |

### Signing out

After `POST /api/devices/{id}/logout` the device's token gets no new socket ticket. The Companion relies on that
when it reconnects; how it learns about the sign-out is not frozen, see below.

### Relied on, not frozen yet

The Companion also uses the following, which this page does not freeze:

- `DeviceSessionRevokedEvent` `{}`: the Companion signs out on it. The host queues it when a device is signed out or
  removed or all sessions are ended, but aborts the device's sockets before the queued push is written, so it does
  not arrive today. That is a host bug, to be fixed before the push is frozen.
- The accepted answer of `OpenFolderUiSession`: no built-in folder view draws a folder as a session.
- `HostLockStateChangedEvent` with `"locked":true`: only a locked desktop session sets it.
- `NotificationShownEvent`, `NotificationUpdatedEvent` and `NotificationClosedEvent`, and the video stream pushes
  `VideoStreamCatalogChangedEvent`, `VideoStreamSessionChangedEvent` and `VideoStreamSessionClosedEvent`.
- `GET /api/system/version`, `GET /api/device-setup` and its certificate download, `/api/companion/scripts`,
  `/api/companion/commands` and `/api/companion/screenshots`, and `POST /api/auth/login`, `/api/auth/refresh`,
  `/api/auth/logout` and `/api/auth/identity`.
