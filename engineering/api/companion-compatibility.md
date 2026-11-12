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
`network/HostWire.kt`, `deck/DeckClient.kt`, `deck/WidgetSessions.kt`, `deck/SessionRecovery.kt`,
`deck/CompanionReporter.kt`). How the app times out, retries and recovers is its own contract,
`docs/integration/macro-deck-host.md` in the Companion repository. Every vector is replayed against a real host
by [CompanionWireCompatibilityTests](../../host/tests/MacroDeckHost.Tests.SystemTests/CompanionWireCompatibilityTests.cs).

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
- `UiSessionInvalidatedEvent` `{"sessionId","code","message","retryable"}`: shape only, not replayed yet.

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

### Relied on, not frozen yet

The Companion also uses the following, which this page does not freeze yet:

- Requests: `GetDeviceScreenSaver`, `OpenScreenSaverUiSession`, `OpenFolderUiSession`, `OpenModalUiSession`,
  `CompleteUiModal`, `SyncCompanionLicense`, `RespondToNotification` and the video stream requests.
- Pushes: `UiSessionInvalidatedEvent` (shape above), `WidgetsCreatedEvent`, `WidgetUpdatedEvent`,
  `WidgetsUpdatedEvent`, `WidgetDeletedEvent`, `WidgetsDeletedEvent`, `WidgetPositionsUpdatedEvent`,
  `WidgetStateInvalidatedEvent`, `FolderUpdatedEvent`, `FolderDeletedEvent`, `FolderNavigationEvent`,
  `ProfileUpdatedEvent`, `ProfileDeletedEvent`, `ActionExecutionStatusEvent`, `UiModalOpenedEvent`,
  `LocalizationCultureChangedEvent`, `LocalizationCatalogChangedEvent`, `AppearanceChangedEvent`,
  `HostLockStateChangedEvent`, `DeviceScreenSaverChangedEvent`, `ShowDeviceScreenSaverEvent`,
  `DeviceSessionRevokedEvent`, `CompanionCommandEvent`, `CompanionLicenseEvent` and `CompanionLicenseRevokedEvent`.
- REST reads: `/api/folders`, `/api/profiles`, `/api/localization`, `/api/settings/appearance`,
  `/api/system/lock-state`, `/api/system/time`, `/api/system/fonts` and `/api/ui/resources`.
