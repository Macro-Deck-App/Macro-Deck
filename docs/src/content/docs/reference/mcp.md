---
title: MCP server
description: The Model Context Protocol server built into the Macro Deck host - transport, access tokens, the macrodeck-mcp/1 tool contract, how secrets stay out of reach, and the error codes a client sees.
---

The Macro Deck host runs a [Model Context Protocol](https://modelcontextprotocol.io) server so an AI
client - Claude, VS Code or any other MCP client - can read a user's setup, build widgets and flows,
run actions and store secrets **without ever reading a secret value**. This page is the contract for
client and tool authors. The reasoning is in
[ADR 0113](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0113-the-mcp-server-is-an-untrusted-principal.md).

## At a glance

```http
POST /mcp HTTP/1.1
Host: 127.0.0.1:<public port>
Authorization: Bearer mdmcp_<token>
Content-Type: application/json
Accept: application/json, text/event-stream

{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"catalog","arguments":{"kind":"actions"}}}
```

| | |
| --- | --- |
| Transport | Streamable HTTP at `/mcp` |
| Listeners | The public listener (the port in **Settings > Network**, reachable from the local network) and the private loopback listener the desktop app and the Claude Desktop extension use |
| Protocol revisions | `2026-07-28` (stateless, elicitation as multi round-trip results) and `2025-11-25` and earlier (a session with `Mcp-Session-Id`) on the same endpoint |
| Credential | An MCP access token the user creates in **Settings > AI clients** |
| Contract | `macrodeck-mcp/1`, versioned in every tool's `_meta.contractVersion` |

The address is shown in **Settings > AI clients**: `http://127.0.0.1:<public port>/mcp` on the same
computer, or the computer's network address instead of `127.0.0.1` from another one. The public port
only changes when the user changes it. It is plain HTTP, so on an untrusted network a token can be read
in transit; revoke it if that may have happened. When HTTPS is turned on for network connections, the
HTTPS port serves `/mcp` as well.

## Access tokens

A token is created, listed, revoked and removed by the user in **Settings > AI clients**. The
plaintext (`mdmcp_` followed by 64 hex characters) is shown once; the host stores only a SHA-256
hash. A token does not expire; revoking it rejects its next request and closes the requests and sessions
it has open.

The token is accepted **only** on `/mcp`:

- It authenticates nothing under `/api` and nothing on the UI or plugin WebSockets.
- An admin, client or plugin credential, and the desktop app's loopback secret, are not accepted on
  `/mcp`.
- A session created with one token cannot be continued with another (`403`).

## Claude Desktop extension and stdio bridge

For clients that start a local stdio server instead of speaking HTTP, the Macro Deck desktop app's own
executable doubles as a bridge:

```sh
MACRODECK_MCP_TOKEN=mdmcp_<token> <Macro Deck executable> mcp
```

It reads newline-delimited JSON-RPC on stdin, forwards it to `/mcp` with the token as bearer credential,
and writes the responses and server requests (elicitations included) to stdout. Diagnostics go to stderr.

- It never starts Macro Deck or opens a window. It finds the running host through the loopback port file
  in the app's configuration directory, and sends the token only after that host proved the desktop app's
  loopback secret. Nothing depends on `TMPDIR`, which Claude Desktop does not pass on.
- When the session is lost (the host restarted, also on another port), it re-initializes with the
  client's original `initialize` and replays a request only when it provably never reached the host. A
  request that was in flight is answered with a JSON-RPC error instead, so a tool call never runs twice.
- Without a host, with a rejected token (`401`) or while `/mcp` is unavailable (`404`), every request
  gets a JSON-RPC error that says so, in the operating system's language. Without `MACRODECK_MCP_TOKEN`
  it exits with status 2.
- For development, a host started without the desktop app can be reached by setting both
  `MACRODECK_HOST_PORT` and `MACRODECK_LOOPBACK_SECRET_FILE` (a file holding that host's loopback secret).
  The pair is honoured only because the environment comes from the user's own client configuration, and
  the port still has to prove that secret before the token is sent. Setting only one of them has no
  effect.

**Download for Claude Desktop** in **Settings > AI clients** builds a
[Claude Desktop Extension](https://github.com/modelcontextprotocol/mcpb) (`.mcpb`, manifest version 0.3)
of type `binary` that runs this bridge from the installed app, with the new token in its `env`. It is
offered on macOS and Windows when the host was started by the installed desktop app. The admin endpoint
behind it is `POST /api/mcp/tokens/{id}/claude-desktop-extension` with `{ "token": "<plaintext>" }`;
it answers `application/zip`, or the settings' JSON error body with `invalid_token`, `token_revoked` or
`extension_unavailable`. `GET /api/mcp/endpoint` reports whether it is offered in `extensionAvailable`.

## Browser and DNS-rebinding protection

Every request to `/mcp`, the secret page and the confirmation page is refused unless:

| Check | Refusal |
| --- | --- |
| On the loopback listener: it comes from a loopback address | `404` |
| On the loopback listener: `Host` is `localhost` or a loopback address | `403` |
| `Origin` is absent or the listener's own origin, and `Sec-Fetch-Site` is absent, `same-origin` or `none` | `403` |

Native MCP clients send no `Origin`, so they pass. A web page in a browser cannot reach the endpoint,
neither through the network address nor through a hostname that resolves to this computer. On the
public listener the MCP token is the only credential; nothing else limits which computer connects.

While the host's key ring is locked, `/mcp` and the secret page are not mapped and answer `404`.

## Secrets

No tool returns a secret value, and none can make the host decrypt one on the client's behalf.

- Secrets appear only as references: `{"$secret":"<id>"}`. A parameter marked `secret: true` in the
  catalog takes such a reference as its value.
- There is no tool for revealing a secret, exporting with secrets, backups, the backup recovery key,
  integration configuration or the key ring.
- `set_secret` asks the user to type the value into a Macro Deck page in their browser (URL-mode
  elicitation). The value never passes through the client; the tool returns only the new reference.

## Tools

Tool names, argument names and result fields are the contract. Titles and descriptions are English
contract text: the model reads them, and localizing them would change the contract with the host's
language. A field may be added to a result; nothing is removed or changes meaning within
`macrodeck-mcp/1`.

Errors are tool results with `isError: true` and a text of the form `CODE: message`.

### catalog

Read-only. What can be built.

| Argument | Type | |
| --- | --- | --- |
| `kind` | `actions` \| `events` \| `widgetTypes` | Optional; all three when omitted |
| `integrationId` | string | Optional; only entries of this integration or provider |

Result: `{ contractVersion, actions?, events?, widgetTypes? }`.

- `actions[]`: `{ integrationId, actionId, integration, name, description, riskCategory, policy, parameters[] }`.
  `riskCategory` is the action's declared category (`none`, `scripts`, `programs`, `input`, `network`,
  `files`, `power`); `policy` is what the [action policy](#action-policy) does with it right now: `allow`,
  `ask-first` or `off`.
- `events[]`: `{ id, providerId, provider, name, description, deliveryKind, configurationParameters[], payloadParameters[] }`
- `widgetTypes[]`: `{ type, providerId, name, description, defaultData, dataSchema }`
- `parameters[]`: `{ name, type, label, description, required, secret, default, options[]?, children[]? }`.
  `default` is always `null` for a secret parameter.

Names and descriptions are in the language the user chose in Macro Deck.

### list

Read-only.

| Argument | Type | |
| --- | --- | --- |
| `entity` | `profiles` \| `folders` \| `widgets` \| `automations` \| `scripts` \| `variables` | Required |
| `profileId` | string | Optional; folders or widgets of this profile |
| `folderId` | string | Optional; widgets of this folder |

Result: `{ entity, items[] }`. Automations and scripts are listed without their flows; use `get`.

### get

Read-only.

| Argument | Type | |
| --- | --- | --- |
| `entity` | as for `list` | Required |
| `id` | string | Required |

Result: `{ entity, item, secretBlocks[] }`. A widget carries its `data` (including action flows), an
automation or a script its `flows`. `secretBlocks[]` is `{ blockId, approved }` for every block that uses a
secret: a block the user has not approved yet does not run its secret. Error `NOT_FOUND` when there is no
such entry.

### set_secret

| Argument | Type | |
| --- | --- | --- |
| `kind` | `password` \| `secret` | A password can later be shown to the user in Macro Deck; a secret never |
| `label` | string, 1-100 characters | What the user sees on the page |

The client must declare the `elicitation.url` capability. The host sends a URL elicitation for a
one-time page `<origin>/mcp/secret-entry/<id>`, valid for 10 minutes and for one
submission. After the user accepted and saved the value, the result is
`{ secret: { "$secret": "<id>" }, kind, label }`.

`<origin>` is the scheme and address the client reached the host on, so the page opens on the computer
the client runs on. Over the public listener the value travels like the token: plain HTTP within the
local network.

| Code | When |
| --- | --- |
| `ELICITATION_UNAVAILABLE` | The client does not support URL elicitation |
| `SECRET_ENTRY_DECLINED` | The user declined to open the page |
| `SECRET_ENTRY_EXPIRED` | The page was not submitted in time, or the request is no longer valid |

### apply_changes

Writes a batch of changes. Everything is validated before anything is written; with `dryRun` nothing is
written at all.

| Argument | Type | |
| --- | --- | --- |
| `changes` | array, 1-100 | `{ op, entity, id?, ref?, data? }`, applied in order |
| `dryRun` | boolean | Validate only, and list what would need the user's confirmation |

| `entity` | `create` data | `update` data | `delete` |
| --- | --- | --- | --- |
| `profile` | `name`, `defaultRows`, `defaultColumns` | same | not supported |
| `folder` | `profileId`, `name`, `parentId` | `name`, `rows`, `columns` | yes, with its widgets |
| `widget` | `folderId`, `type`, `positionX`, `positionY`, `width`, `height`, `data` (object) | same, except `folderId` | yes |
| `automation` | `name`, `description`, `enabled`, `flows` (array) | same | yes |
| `script` | `name`, `description`, `flows` (array), `inputs`, `runsOnWidget` | same | yes |
| `variable` | `name`, `scope` (`global`, `script`, `automation`), `scopeRefId`, `type`, `value` (string, number or boolean) | `value` | yes |

A `create` may carry `ref`, for example `"r1"`. A later change of the same batch names that object as
`"@r1"` in any id field, including ids inside widget data and flows. Moving a widget to another folder,
and changing an object created in the same batch, are not supported in `macrodeck-mcp/1`.

Result: `{ valid, applied, batchId?, problems[], confirmations[], createdIds }`, where
`problems[]` is `{ index, path, code, message }` and `confirmations[]` is `{ index, kind, detail }`.
If any change fails while the batch is applied, the changes already applied are reverted and
`applied` is `false`.

Rules the batch is checked against:

- Every action block that is new or changed against the stored version is checked against the
  [action policy](#action-policy): a provider turned off fails the batch with `PROVIDER_BLOCKED`, one
  that asks first makes the host ask the user once (see [Action policy](#action-policy)) before
  anything is written. This also covers blocks the host runs on its own later: timers, schedules, calendars and
  widget states.
- A `{"$secret":"<id>"}` reference is accepted only if this client created the secret with
  `set_secret` (or the block is unchanged), otherwise `FOREIGN_SECRET_REF`.
- Writes are made as an untrusted writer: a block that uses a secret stays inactive until the user
  approves it in Macro Deck.
- Changing a variable that an approved secret-using block reads asks the user first
  (`protected-variable`), whether the change is a variable `update` or a new or changed block whose
  parameter names that variable. A new or changed block that calls a script with an input an approved
  secret-using block reads asks too (`steering-value`).

### undo

| Argument | Type | |
| --- | --- | --- |
| `batchId` | string | Optional; by default the newest batch of this client that is not undone |

Reverts a batch: created objects are deleted, changed ones get their previous state back, deleted ones
come back with their original ids and their secrets. An object changed after the batch is left as it is
and reported as `ENTITY_CHANGED_SINCE`. A restored folder becomes the start folder again only while the
profile still has the start folder the batch left; a start folder picked after the batch stays and is reported
the same way. Result: `{ undone, batchId, problems[] }`.

The host keeps the last 20 batches of all clients. Restoring a backup clears them.

### run_action

| Argument | Type | |
| --- | --- | --- |
| `integrationId`, `actionId` | string | From `catalog` |
| `parameters` | object | Values by parameter name |

Result: `{ status, errorCode, message, durationMs }`. The [action policy](#action-policy) applies. A
secret parameter takes only a reference this client created, and the action receives it unresolved.

### run_script

| Argument | Type | |
| --- | --- | --- |
| `scriptId` | string | From `list` |
| `inputs` | object | Values by input name |

Every action the script would run, through nested scripts too, is checked against the action policy. An
input that an approved secret-using block reads asks the user first (`steering-value`).

### trigger_event

| Argument | Type | |
| --- | --- | --- |
| `eventId` | string | From `catalog`; scheduled events cannot be triggered |
| `parameters` | object | Payload values by name |

Result: `{ triggered, queuedTriggers }`. Every action of the automations and widget triggers listening to
the event is checked against the action policy, and payload values that an approved secret-using block
reads ask the user first.

## Action policy

The user sets, per provider, whether an AI client may run its actions directly (**Allow**), must ask
first (**Ask first**), or may not run them at all (**Off**), in **Settings > AI clients**. By default:

- a built-in action asks first when it declares a
  [risk category](/features/actions/) (`IActionDefinition.RiskCategory`): scripts, programs and
  commands, keyboard and mouse input, network requests, files, power. Every other built-in action is
  allowed;
- every action of a plugin provider asks first, whatever it declares, and so does any provider or action
  the host does not know right now.

If the stored policy cannot be read, every provider is treated as **Off** until the user saves it again.

Asking first, and the confirmations for protected variables and steering values, use form elicitation
in the client when it declares `elicitation.form` (or an empty `elicitation` capability). A client that
declares only `elicitation.url` gets a URL elicitation for a one-time Macro Deck page
`<origin>/mcp/confirm/<id>` instead, with the same guards as the secret page, valid for 5 minutes and
for one answer. The page names the token, the
actions with their provider, for run_action also its values (secret values only as "a secret"), and offers **Allow** and
**Decline**; once it is answered the host sends `notifications/elicitation/complete`. The answer is bound
to that exact call either way.

| Code | When |
| --- | --- |
| `CONFIRMATION_DECLINED` | The user said no, did not open the page, or did not answer it in time |
| `CONFIRMATION_UNAVAILABLE` | The client declares neither form nor URL elicitation |

## Variables and attribution

While an action runs for an MCP client, and for 30 seconds after, a write by that action's provider to a
variable that an approved secret-using block reads is refused with `VARIABLE_APPROVAL_REQUIRED`, unless the
user confirmed that variable for this call. Writes are attributed by provider, so a write the same
provider makes for another reason in that window is refused too, and a write an action makes after the
30 seconds is not attributed.

### Common error codes

| Code | When |
| --- | --- |
| `INVALID_ARGUMENT` | An argument is missing or out of range |
| `NOT_FOUND` | No entry with that id |
| `PROVIDER_BLOCKED` | The action's provider is turned off for AI clients |
| `FOREIGN_SECRET_REF` | A secret reference this client did not create |
| `CONFIRMATION_DECLINED`, `CONFIRMATION_UNAVAILABLE` | See [Action policy](#action-policy) |
| `VARIABLE_APPROVAL_REQUIRED` | See [Variables and attribution](#variables-and-attribution) |
| `REF_UNRESOLVED`, `ACTION_NOT_FOUND`, `PARAMETER_REQUIRED`, `EVENT_NOT_FOUND`, `WIDGET_TYPE_NOT_FOUND`, `INVALID_DATA`, `UNSUPPORTED`, `APPLY_FAILED`, `ENTITY_CHANGED_SINCE` | Problems reported by `apply_changes` and `undo` |

## See also

- [Security model](/policies/security/#mcp-clients) - where an MCP client sits in the trust model.
- [Compatibility policy](/policies/compatibility/) - what `macrodeck-mcp/1` promises.
- [MCP specification](https://modelcontextprotocol.io/specification) - transport, elicitation and
  multi round-trip requests.
