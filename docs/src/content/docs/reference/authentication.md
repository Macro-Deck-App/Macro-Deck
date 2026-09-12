---
title: Authentication
description: How a plugin obtains a credential, exchanges it for a session token, presents that token on REST and on the WebSocket, and what it must never do with either.
---

A plugin authenticates twice, with two different things. A **credential** - a launch bootstrap token
or a per-plugin secret - identifies the plugin to the host and buys a session. A **session token**
authenticates every request for the life of that session. This page covers where each comes from, how
each is presented, and how long each lasts.

The SDK does all of this for you: `MacroDeck.Plugin.Hosting` registers, exchanges, reconnects and
resumes without a plugin author writing a request. Read this page when you are implementing the
protocol in another language, when something in the handshake is failing, or when you need to know
what a credential does and does not grant. See [ADR 0028](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0028-plugin-credentials-and-pairing.md)
for why the model looks like this.

## The two credential kinds

Which credential a plugin holds follows directly from its registration mode.

| Mode | Credential | Where it comes from |
| --- | --- | --- |
| Managed | A **launch bootstrap token**, minted per launch | The host's supervisor mints it and injects it as `MACRO_DECK_PLUGIN_SECRET`. A managed plugin never calls the registration endpoint and persists nothing. |
| Self-registering | A **per-plugin secret** | By default, the plugin obtains one through interactive pairing: it creates a pairing request, the desktop app shows an approval prompt, and redemption returns the secret. An explicitly supplied Developer token is exchanged instead through `POST /api/plugins/registration`, for headless and automated setups. Either way, the plugin persists the secret and reuses it on every later run. |

The mode is taken from `UseRegistrationMode` if called, then from `MacroDeck:Plugin:Mode`
(`MACRO_DECK_PLUGIN_MODE`), and is otherwise inferred: an id and a secret both present means the host
supplied them, so the mode is `Managed`. See
[registration modes](/reference/plugin-hosting/#registration-modes) for the full environment-variable table.

### Managed: the launch bootstrap token

The supervisor mints a cryptographically random token for one launch and passes it in the child
process's environment alongside the plugin id, the host URL and a fresh instance id. The token is held
in memory host-side, never persisted, and is valid for **two minutes while unused** - the clock only
matters until the token is first acquired, so a plugin that connects promptly and then runs for days is
unaffected.

`MACRO_DECK_PLUGIN_LAUNCH_ID` travels with it, but it is diagnostic and log-correlation only: the host
never reads it back and it is never asserted on the wire.

### Self-registering: interactive pairing

By default, a self-registering plugin needs no credential the developer creates, copies or manages at
all. Press F5, approve a prompt in the desktop app, done.

1. The plugin creates a pairing request: `POST /api/plugins/pairing` with `{ pluginId, displayName,
   codeChallenge, codeChallengeMethod: "S256", client: { executablePath?, processId?, sdkVersion? } }`.
   `codeChallenge` is `base64url(SHA-256(codeVerifier))` for a high-entropy verifier the plugin
   generates and keeps to itself - the same PKCE shape OAuth uses to prove that whoever redeems a
   request is whoever created it. The host stores only the challenge, never the verifier. A successful
   create returns `201 { requestId, expiresAt, pollIntervalSeconds }`.
2. The desktop app shows an approval prompt naming the plugin id and display name, and the reported
   `executablePath`, `processId` and `sdkVersion` - labelled as self-reported and unverified, because
   they come from the requesting process rather than from the connection. Whether the request arrived on
   the public listener is verified by the host and shown as such.
3. The plugin polls `GET /api/plugins/pairing/{requestId}`, which answers `200 { status, expiresAt }`
   with `status` one of `pending`, `approved`, `rejected` or `expired`. An unknown or pruned
   `requestId` also answers `expired` rather than `404`, so polling code has one state machine instead
   of a special-cased not-found branch.
4. Once `status` is `approved`, the plugin redeems it: `POST
   /api/plugins/pairing/{requestId}/redemption` with `{ codeVerifier }`, returning `201 { pluginId,
   pluginSecret }`. The secret is minted here, at redemption - never at approval, so an approval the
   plugin never gets around to redeeming leaves any existing working credential untouched. A wrong
   verifier does not consume the request, so a retry with the correct one still succeeds; a successful
   redemption consumes the request permanently, so replaying it fails.

Pairing needs no authentication header at all - there is no credential to present yet. It is gated by
transport reachability (loopback only, same as every plugin endpoint) and by **Developer Mode**, a host
setting off by default and reachable in the desktop app's settings. Developer Mode is a continuous
condition, not just a creation-time gate: turning it off invalidates redemption of a request that was
already approved.

Only one live pairing request per plugin id is allowed; creating a second while one is still pending
answers `429`, rather than replacing the visible request out from under the developer looking at it. A
global cap on pending requests also answers `429` with `Retry-After`, and both creation and redemption
are separately rate-limited.

If the host already holds an active registration for the plugin id but the local credential is gone -
the state directory was deleted, or a different machine was used before - the approval prompt offers to
**replace the development credential**, with a separate confirmation from a plain approval. Redemption
then rotates the secret in place and terminates the plugin's live sessions, so there is never an instant
where both the old and new secret authenticate. This is the supported recovery for a missing local
credential; see [ADR 0028](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0028-plugin-credentials-and-pairing.md)
for why the rotation happens at redemption rather than at approval.

The secret is written to `<state>/<pluginId>/credentials.json`, where `<state>` defaults to
`%LOCALAPPDATA%\MacroDeck\plugins`, `~/Library/Application Support/MacroDeck/plugins`, or
`$XDG_STATE_HOME/macro-deck/plugins` (falling back to `~/.local/state/macro-deck/plugins`). The file is
written to a temporary file made owner-only *before* anything is written to it and then moved into
place, so there is no window in which the secret is readable by anyone else and no way for a crash to
leave a half-written file. On Windows the per-user profile ACL is the protection; **nothing is
encrypted at rest**.

`GET /api/plugins/protocol` advertises pairing support through an optional `pairing` block (`supported`,
`requestLifetimeSeconds`, `pollIntervalSeconds`, `developerModeEnabled`). Its absence means the host
predates pairing; the SDK falls back to telling the developer to use a Developer token instead.

`supported` says that the host implements pairing at all. `developerModeEnabled` says whether it would
accept a request *right now*, and is the one part of the block that can change while the host runs;
absent means the host does not report it, which is not the same as `false`.

The SDK creates a pairing request at most once per process lifetime and treats rejection, expiry and an
unsupported host as fatal rather than retryable, so it never gets stuck silently re-prompting - see
[Debugging plugins](/guides/debugging/) for what a fatal pairing outcome looks like and how to recover.

Developer Mode being off is the one exception. A host that reports `developerModeEnabled: false` is
refused *before* a request exists, so no prompt has been shown and nothing is waiting on a person: the
SDK creates no request, keeps retrying on its ordinary reconnect backoff, and pairs as soon as Developer
Mode is enabled - without restarting the plugin or Macro Deck. Against a host that does not report the
field the SDK still has to attempt the request to find out, and that refusal stays fatal, because the
host counts every refused attempt against a rate limit shared by all plugins.

### Configuring pairing in the SDK

Two additive options control whether and how long the SDK attempts pairing, both optional:

| Option | Environment variable | Default |
| --- | --- | --- |
| `MacroDeck:Plugin:PairingEnabled` | `MACRO_DECK_PLUGIN_PAIRING` | `true` |
| `MacroDeck:Plugin:PairingTimeout` | `MACRO_DECK_PLUGIN_PAIRING_TIMEOUT` | Unset - the host-advertised `requestLifetimeSeconds` is used |

Turning pairing off (`PairingEnabled: false`) with no stored credential and no Developer token is a fatal
startup condition: "This plugin has no stored credentials, and interactive pairing is disabled." Setting
`PairingTimeout` bounds how long the SDK waits for approval independently of what the host advertises;
leaving it unset defers entirely to the host's own request lifetime.

## Advanced: the Developer token, for headless and automated development

CI runners, build scripts and other unattended setups have no human available to approve a pairing
prompt. For those, a self-registering plugin can still use a reusable Developer token.

A user creates a Developer token in the desktop app under **Developer Tools → Plugin development →
Credentials** - a name and an optional expiry. Its scope is `plugin:enroll`, the only Developer-token scope defined today. The
plaintext is shown exactly once; the host stores only its hash.

Set it as `MACRO_DECK_PLUGIN_ENROLLMENT_TOKEN`. On first start the plugin calls
`POST /api/plugins/registration` with it and receives a per-plugin secret in return - also shown
exactly once, also stored host-side only as a hash. The SDK persists that secret before it is ever used
and enrols again only if it is gone. An explicitly supplied enrollment token takes precedence over
pairing in the SDK's credential resolution order, which is what lets an automated run skip the
interactive prompt entirely.

The secret this path produces is written to the same `<state>/<pluginId>/credentials.json` file, with
the same owner-only handling described above.

Minting a Developer token is an admin operation the desktop app performs against
`api/plugin-tokens`. That is not a plugin-facing endpoint, and the CLI has no command that mints one.

## The endpoints

| Endpoint | Authenticated by | Success |
| --- | --- | --- |
| `GET /api/plugins/protocol` | Nothing | `200` with the protocol descriptor - versions, capability kinds, limits, timeouts |
| `POST /api/plugins/pairing` | Nothing (loopback + Developer Mode) | `201` with `{ requestId, expiresAt, pollIntervalSeconds }` |
| `GET /api/plugins/pairing/{requestId}` | Nothing | `200` with `{ status, expiresAt }` |
| `POST /api/plugins/pairing/{requestId}/redemption` | The PKCE `codeVerifier` in the body | `201` with `{ pluginId, pluginSecret }` |
| `POST /api/plugins/registration` | `X-MacroDeck-Enrollment-Token` (+ Developer Mode) | `201` with `{ pluginId, pluginSecret }` |
| `POST /api/plugins/sessions` | `X-MacroDeck-Plugin-Id` + `X-MacroDeck-Plugin-Secret` (+ Developer Mode, for a development credential) | `201` with the session response below |
| `GET /plugins/ws` | `Authorization: Bearer <sessionToken>` | `101 Switching Protocols` |
| `DELETE /api/plugins/sessions/{sessionId}` | `Authorization: Bearer <sessionToken>` | `204` |
| `DELETE /api/plugins/registration/{pluginId}` | An admin bearer token | `204` - the revocation path for a compromised credential |

Developer Mode gates every path that depends on a development credential, not just pairing: enrolling
with a Developer token and opening a session with the secret that enrolment returned both answer `403`
while the switch is off, with `reason: "developer_mode_disabled"` in the error's `details`. It is a
live switch rather than a start-up option - turning it off disconnects development plugins already
connected, and turning it on makes them workable again without restarting Macro Deck. Stored
credentials survive either way. A plugin Macro Deck installed and launches itself authenticates with a
host-issued launch token and is never gated.

Discovery needs no authentication deliberately, so a plugin can decide whether and how to register
before it holds any credential. It also reports the switch, in `pairing.developerModeEnabled` and
`enrollment.developerModeEnabled`, so a plugin can say what is wrong instead of surfacing a bare `403`. The paths are unversioned: the protocol version is negotiated over
`POST /api/plugins/sessions`, never encoded in a URL. The full REST surface, request and response
schemas included, is [the OpenAPI spec](/specs/openapi.yaml) and is rendered under
[REST API](/reference/rest/).

One session endpoint serves both credential kinds: the host tries the launch-token store first and then
the registration store, so a managed plugin and a self-registering one exchange credentials the same
way.

## The session exchange

`POST /api/plugins/sessions` is where negotiation happens, once, authoritatively. The request carries
the plugin's requested protocol version range, its declared capabilities, optionally its declared name
and version, and optionally an `sdk` block describing the SDK it was built against.

The response carries:

| Field | What it is |
| --- | --- |
| `sessionId` | The session's id - a canonical lowercase dashed UUIDv7 |
| `sessionToken` | The bearer token for this session |
| `negotiatedVersion` | The single protocol version both sides speak |
| `capabilities` | The per-kind negotiation result, including any rejections |
| `limits`, `timeouts` | The values this host enforces - read them rather than hard-coding them |
| `compatibility` | Optional deprecation/compatibility report - see [deprecations](/policies/deprecations/) |

The session token is a JWT signed by the host, carrying a scope claim of `plugin` plus the plugin id
and session id. `plugin` is authorised on its own policy: a plugin session token does not grant client
or admin access, and neither an admin token nor a client token is accepted where a plugin token is
required. See [ADR 0003](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0003-loopback-trust-token-scopes-and-device-identity.md).

## Presenting the token

**On REST**, and on the WebSocket upgrade, as `Authorization: Bearer <sessionToken>`.

**On the WebSocket**, the same `Authorization` header on the upgrade request, together with the
`macrodeck.plugin.v1` subprotocol offered through `Sec-WebSocket-Protocol`. The host requires the
subprotocol and authenticates *before* accepting the upgrade; a failure is a `401` carrying a
`ProtocolError` body, not an accepted socket that closes afterwards.

Not a cookie, and not the query string. A cookie must never be used here: a browser cannot set an
`Authorization` header on a `WebSocket` handshake, so relying on a cookie would be only accidentally
safe against cross-site WebSocket hijacking. A query string lands in logs.

The first message on the socket, `session.hello`, carries **no credential**. It only asserts the
already-negotiated protocol version and session id; a mismatch is `PROTOCOL_VERSION_UNSUPPORTED` and
closes the socket with `4001`.

### Header vocabulary

| Header | Used on |
| --- | --- |
| `X-MacroDeck-Enrollment-Token` | `POST /api/plugins/registration` only |
| `X-MacroDeck-Plugin-Id` | `POST /api/plugins/sessions` only |
| `X-MacroDeck-Plugin-Secret` | `POST /api/plugins/sessions` only - never on the WebSocket upgrade |
| `Authorization: Bearer …` | Every request that needs a session token, and the WebSocket upgrade |

Pairing carries no credential in a header at all: the codes involved (`codeChallenge`, `codeVerifier`)
travel in the request body, and `requestId` travels in the URL because it authorizes nothing on its own.
Pairing's three endpoints are gated by loopback reachability and Developer Mode instead of by a header.

## Lifetime and renewal

| Thing | Lifetime |
| --- | --- |
| Launch bootstrap token | 2 minutes while unused; the expiry stops mattering once it has been acquired |
| Pairing request | Host-advertised, short-lived (`expiresAt` from the create response); memory-only, so a host restart discards every pending or approved request regardless of its remaining time |
| Per-plugin secret | No independent expiry. Revoked on uninstall or explicitly through `DELETE /api/plugins/registration/{pluginId}`. A secret obtained through a Developer token also stops authenticating when that token expires or is revoked; a secret obtained through pairing has no such dependency. |
| Developer token | Optional expiry, chosen when it is created; none by default. Expiry or revocation prevents every registration it minted from opening a new session. |
| Session token | 15 minutes |
| Session resume window | 60 seconds after the socket drops |

**There is no refresh endpoint for the session token, deliberately.** The resume window is a minute and
the token lasts fifteen, so the window is always the binding constraint: any reconnect too late to
resume is already opening a new session, which issues a new token anyway. To renew, run the session
exchange again.

Expiry is not the only thing that ends a session. The host re-checks the live session registry on every
request, so terminating a session takes effect immediately even though its token stays cryptographically
valid for the rest of its 15 minutes.

The SDK reconnects with full-jitter exponential backoff (1 second initial, 30 seconds maximum, factor
2), resumes inside the resume window, and tolerates a bounded number of authentication failures before
treating them as fatal - `MacroDeck:Plugin:MaxAuthenticationFailures`, default 3. A persisted secret is
never discarded automatically.

## Reachability

Every plugin endpoint is served on both the public listener and the private loopback listener, but only
accepts a **loopback remote address**; a caller on the LAN is refused with `403` regardless of which
port it used, and so is a request that looks like it came from a browser. A self-registering plugin
must therefore run on the same machine as the host.

## What a plugin must never do

- **Never send the plugin secret anywhere but `POST /api/plugins/sessions`.** It buys the session token
  and its job is done. It must not appear on the WebSocket upgrade, in a query string, or in a log
  line.
- **Never put the session token in a cookie or a URL.** The `Authorization` header, and nowhere else.
- **Never persist a plaintext Developer token.** It is shown once so a user can paste it into a
  plugin's configuration; a caller that displays it should discard it.
- **Never assert identity on the wire and expect it to be trusted.** The host checks the credential
  against its own token record, never against anything the plugin claims. `MACRO_DECK_PLUGIN_LAUNCH_ID`
  in particular is yours to log and nothing more.
- **Never treat a managed launch token as reusable state.** It is minted per launch; persisting it
  gains nothing and the managed credential store refuses to save it.
- **Never send the PKCE `codeVerifier` anywhere but the redemption call.** `requestId` is safe in a URL
  because it authorizes nothing by itself; the verifier is what proves possession, so treat it the same
  as any other secret in transit.

## Authentication errors

Every authentication failure produces one byte-identical error: code `UNAUTHENTICATED`, `retryable`
false, no `details`, HTTP `401`. An unknown plugin id, a wrong secret, a spent or expired bootstrap
token, a revoked registration, an unknown or expired Developer token, and a missing header are all
indistinguishable by design - there is no oracle to probe.

Pairing redemption follows the same principle: an unknown request, an expired one, one that was never
approved, one already redeemed, and a wrong verifier all answer `401` identically. `GET
/api/plugins/pairing/{requestId}` is where a plugin actually learns its status - it distinguishes
`pending`, `approved`, `rejected` and `expired`, and answers `expired` rather than `404` for an unknown
or pruned request id, so polling never has to special-case a not-found response.

A `403` uses the same `UNAUTHENTICATED` code and is distinguished only by HTTP status: the wire error
vocabulary carries no dedicated "forbidden" value. It is what a non-loopback caller, a browser-shaped
request, or a token naming a different session receives - and, on the pairing endpoints specifically,
what a request receives while **Developer Mode** is off.

| Situation | Code | Where |
| --- | --- | --- |
| Any authentication failure | `UNAUTHENTICATED` | REST `401` |
| Non-loopback or browser caller, wrong session | `UNAUTHENTICATED` | REST `403` |
| Developer Mode off (pairing creation or redemption) | `UNAUTHENTICATED` | REST `403` |
| Identity already registered | `PLUGIN_ALREADY_REGISTERED` | REST `409` |
| No negotiable protocol version | `PROTOCOL_VERSION_UNSUPPORTED` | REST `422`, WebSocket close `4001` |
| Too many attempts | `RATE_LIMITED` with `details.retryAfterSeconds` | REST `429` with `Retry-After` |
| Session expired | `SESSION_EXPIRED` | WebSocket close `4002` |
| Authentication failed on the socket | — | WebSocket close `4003` |
| Session replaced by a newer connection | `SESSION_REPLACED` | WebSocket close `4000` |

Enrollment is rate-limited on one shared bucket; session exchange is rate-limited per plugin id. Pairing
creation and redemption are each separately rate-limited, and creating a request for a plugin id that
already has a live one, or exceeding the global pending-request cap, also answers `429`.

The SDK treats close codes `4002` and `4003` as retryable - dropping the cached session so the next
attempt opens a fresh one - up to `MaxAuthenticationFailures`, after which repeated authentication
failure is fatal. See [what the SDK does for you](/reference/plugin-hosting/#what-the-sdk-does-for-you).

## See also

- [Debugging plugins](/guides/debugging/) - press F5 and approve the pairing prompt, or use the
  headless enrollment flow, then reuse the persisted development credential from an IDE launch profile.
- [Plugin hosting](/reference/plugin-hosting/) - the registration modes and the environment the supervisor
  injects.
- [Plugin protocol](/reference/protocol/) - the wire contract these credentials open.
- [WebSocket reference](/reference/websocket/) - the message catalogue on the other side of the
  upgrade.
- [Security model](/policies/security/) - the trust boundary and its limits.
- [OpenAPI spec](/specs/openapi.yaml) - the machine-readable REST surface.
