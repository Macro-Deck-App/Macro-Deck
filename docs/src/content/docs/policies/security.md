---
title: Security model
description: The trust boundary around a Macro Deck plugin, what the host does and does not guarantee, how credentials and artifacts are handled, and the limits of the model.
---

What the host enforces around a plugin, and what it does not. Use it to decide whether to install
someone else's plugin, or what your own plugin can be trusted with. Anything declared but not enforced,
or shipped but not implemented, is stated as such.

## The trust model at a glance

| Party | Trusted for | Not trusted for, or the limit |
| --- | --- | --- |
| Host | Everything: it owns state, credentials, secrets and install decisions | - |
| Desktop app, over the private loopback port | **Admin**, with a per-launch secret instead of a token | A process running as the same user can read that secret - see [loopback trust](#loopback-trust-needs-a-per-launch-secret) |
| Plugin process | Scope `plugin`: the plugin protocol surface, and nothing else | Not sandboxed: it runs with the user's full privileges. Declared permissions are [not enforced](#permissions-declared-not-enforced), except `host:adb`, `host:companion-devices` and `host:desktop-notifications` |
| Deck clients (web client, companion) | Scope `client`: the viewer-safe endpoints | Plugin endpoints refuse them |
| LAN callers and browsers | The public listener's API and web client | Every plugin endpoint refuses them |
| MCP clients (AI assistants) | Scope `mcp`: the tools of [the MCP server](/reference/mcp/), on the public and the loopback listener | Never a secret value; see [MCP clients](#mcp-clients) |
| Creator Portal and Store | Signing Store plugins after checking the publishing workflow's provenance; publishing the signed registry and its revoked keys | Revocation refuses new installs and updates, not a plugin that is already installed |

## Network exposure

Your plugin's own listener binds `127.0.0.1:0` by default, and the host's plugin endpoints answer only
from loopback. Exposing custom endpoints of your plugin to the network is a separate, explicit security
decision.

| Listener | Reachable from | Notes |
| --- | --- | --- |
| Public listener | The LAN | Serves the API and the web client. HTTPS is optional and configurable - see [ADR 0040](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0040-public-listeners-and-tls.md). While its plain-HTTP port is serving, the host announces its instance name, version and that port on the LAN over mDNS (`_macrodeck._tcp`), unless this is turned off in Settings > Network - see [ADR 0082](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0082-lan-discovery-uses-the-platform-responder.md). |
| Private loopback listener | The local machine only | Never gets TLS: the desktop shell and the plugin SDK reach it over plain HTTP on `127.0.0.1`. |

| Rule | Consequence |
| --- | --- |
| Plugin endpoints are served on both listeners but accept only a loopback remote address | A LAN caller is refused whichever port it used. A self-registering plugin must run on the same machine as the host ([ADR 0028](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0028-plugin-credentials-and-pairing.md)) |
| A browser-shaped request (cross-site `Origin` or `Sec-Fetch-Site`) is refused | Plugin paths are also excluded from the app-wide CORS policy |
| Enabling HTTPS on the public listener does not widen this | The local-only gate grants no principal of its own: reaching it from another listener adds reachability, not authority |
| `/_macrodeck/*` on the plugin's listener is reserved (`/_macrodeckery` is not) | Middleware added with `Configure` runs after the SDK's and cannot answer there; a constant path there is analyzer error MDP2005. See [reserved routes](/reference/plugin-hosting/#reserved-routes) |

### Loopback trust needs a per-launch secret

A request on the *private* loopback port, from a loopback address, with a loopback `Host` header, is
authenticated as **admin** only when it also presents a secret the desktop app generates for every
launch: the app itself sends it as a header, and its window holds a session cookie derived from it. That
is how the desktop UI works without handling a token.

Reaching the port is therefore not enough: another account on the same computer, a local tool that
fetches URLs for someone else, a sandboxed process or a browser tab gets no admin rights there.
[ADR 0098](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0098-loopback-trust-requires-a-per-launch-secret.md) states the limit that remains: **a hostile process running as the same user** can
read the secret, as can anything with that user's file access, such as WSL on Windows, and is outside
what this boundary defends against. The `Host` header check is a
DNS-rebinding guard, and a request a browser marks as cross-site is refused even with the cookie.

Plain HTTP on the LAN leaves tokens visible to on-path attackers, so the public listener can be configured
for TLS. The shipped posture is a self-signed certificate: it "exists to encrypt a link the user already
trusts, not to prove identity to strangers".

### Scopes

Authorisation is deny-by-default: an endpoint with no authorisation metadata is admin-only.

| Scope | Grants |
| --- | --- |
| `admin` | Everything, including the plugin installation surface |
| `client` | The viewer-safe endpoints a deck client needs |
| `plugin` | The plugin protocol surface, and nothing else |
| `plugin:enroll` | Developer (enrollment) tokens - the only Developer-token scope defined today |

`plugin` is a peer scope, not a subset. A plugin session token does not grant `client` access to the
deck-viewing endpoints, and neither an admin token nor a client token is accepted where a plugin token is
required.

## Credentials and tokens

A plugin holds one credential to buy a session, then sends the session token on every request. The
mechanics are in [the authentication guide](/reference/authentication/); this is how they are stored,
revoked, and must be handled.

```http
# Right: the session token in the Authorization header, the secret only on the session exchange
POST /api/plugins/sessions HTTP/1.1
X-MacroDeck-Plugin-Id: com.example.ref-probe
X-MacroDeck-Plugin-Secret: <plugin-secret>

GET /plugins/ws HTTP/1.1
Authorization: Bearer <session-token>

# Wrong: a token in the query string lands in logs
GET /plugins/ws?access_token=<session-token> HTTP/1.1
```

| Credential | Where it is kept | Rules |
| --- | --- | --- |
| Launch bootstrap token (managed) | Host: memory only, keyed by hash. Minted per launch, discarded on every exit path | A process that outlives its own termination sequence can never present a token the host still recognises. The managed credential store refuses to save it |
| Developer token | Host: SQLite, as a SHA-256 hash. Plaintext shown exactly once | Never persist the plaintext; a caller that displays it should discard it |
| Per-plugin secret | Host: SQLite, as a SHA-256 hash, plaintext shown once. Plugin: `credentials.json` in its state directory, **not encrypted at rest** | Sent only to `POST /api/plugins/sessions` - never on the WebSocket upgrade, in a query string or in a log line |
| Session token | Plugin memory; 15-minute JWT | `Authorization` header only - never a cookie or a URL |
| PKCE `codeVerifier` | Plugin memory | Sent only to the redemption call |

- **Hashing.** Verification is constant-time. A plain SHA-256 hash is used deliberately: Developer tokens
  and per-plugin secrets are high-entropy random values, unlike user passwords, which use PBKDF2.
- **The plugin's copy of its secret** is written owner-only, to a temporary file made owner-only before
  anything is written to it, then moved into place. On Windows the per-user profile ACL is the protection.
- **Revocation cascades.** Revoking a Developer token revokes every registration derived from it and
  terminates those sessions immediately. Revoking one registration terminates that plugin's session. The
  host re-checks the live session registry on every request, so a terminated session stops working at
  once even though its 15-minute JWT is still cryptographically valid.
- **Identity is never taken from a claim.** The host checks the credential against its own record.
  `MACRO_DECK_PLUGIN_LAUNCH_ID` is yours to log and nothing more.
- **Client sessions** (web client, companion) hold an access token - 15 minutes for the admin surface,
  60 days for a deck device - and a refresh token that rotates on every use and lives 365 days. Signing a
  device out in the devices list, removing it, or changing the account password or username is checked on
  every request from then on, so a long device token ends the moment you end the session rather than when
  it expires - every device at once for a credential change. A client session that is not tied to a device keeps the 15 minute token, because there is
  nothing to revoke it by. A rotated refresh token presented again within 30 days of its
  rotation revokes that token's rotation chain - the session it belongs to - and leaves every other session
  of the account signed in. One exception: the token rotated out most recently is accepted once more, from the same device and
  scope, so a client whose rotation response was lost in transit can retry - within a minute of the
  rotation, or, when the host restarted in between, within ten minutes of it starting up - and then only
  for the rotation that restart interrupted, because a restart is exactly when a rotation response goes
  missing. The companion pairs with a six-digit, single-use code
  from the desktop app's network panel: one code at a time, minted only on the loopback listener, cleared
  after five failed guesses from any caller, and throttled globally. See
  [ADR 0083](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0083-companion-pairing-code-and-year-long-refresh.md).

### Interactive pairing

By default a self-registering plugin gets its per-plugin secret by pairing, not from a Developer token:
it creates a request, the user approves it in the desktop app, and the plugin redeems it. Mechanics:
[the authentication guide](/reference/authentication/#self-registering-interactive-pairing).

| Guarantee | Detail |
| --- | --- |
| Loopback only | Pairing endpoints are ordinary plugin endpoints: served on both listeners, reachable only from a loopback remote address |
| Gated by Developer Mode, continuously | Off by default. Turning it off invalidates redemption of an already-approved request, so no stale approval survives it |
| Approved only in the trusted desktop app | The prompt is rendered over the private loopback transport ([ADR 0003](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0003-loopback-trust-token-scopes-and-device-identity.md)). An admin bearer token over the network listener is **not** sufficient to approve: approval is a human act in a specific UI, not an API call |
| Proof of possession | The plugin presents a PKCE-style verifier at redemption; the host stores only the SHA-256 challenge, never the verifier. `requestId` is unguessable but authorises nothing on its own, so it is safe in a URL |
| One-time, short-lived, one per plugin id, capped, rate-limited | Expires on a host-advertised timer; consumed permanently on successful redemption; a second create for a plugin id with a live request is refused with `429` instead of replacing it; a global cap on pending requests; separate rate limits on creation and redemption |
| Nothing persists past a host restart | Requests are memory-only in every state - pending, approved, rejected |
| Self-reported fields shown as unverified; transport origin verified | Executable path, process id, SDK version and display name are shown as reported, labelled unverified, since deriving them from the loopback socket would authenticate nothing. Whether the request arrived on the public listener is verified and shown as verified |
| Replacement is atomic at redemption | If the host holds a registration for the plugin id but the local credential is gone, the prompt offers, with a separate confirmation, to replace it. Redemption rotates the secret in place and terminates live sessions in the same step, so old and new never both authenticate. The secret is minted at redemption, never at approval: an unredeemed approval leaves an existing working credential untouched |

### Accepted residual exposure

Pairing uses the same loopback check as the rest of the plugin protocol, which has no port clause: a device
reaching the loopback address through an `adb reverse` tunnel is indistinguishable at the socket level from
a local process ([ADR 0030](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0030-android-usb-connections-over-adb.md)).
Pairing does not widen that existing exposure. A connection from a USB link without debugging is different:
the host dials it itself and marks it, so it is never taken for a local process and cannot reach pairing
([ADR 0095](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0095-usb-connections-without-debugging.md)).

What pairing changes is what a hostile local process needs. Before, it could mint a Developer token itself
through the then credential-less loopback admin. Now it needs a human to approve a specific prompt while
Developer Mode is on, unless it runs as the same user and reads the desktop app's
[loopback secret](#loopback-trust-needs-a-per-launch-secret). That is bounded by Developer Mode being off by default, approval only over the trusted transport, a
prompt that names what is approved and labels unverified fields, the one-request-per-plugin-id and
rate-limit rules, and nothing being minted before proof of the verifier - a bound, not a cryptographic
guarantee, as recorded in
[ADR 0028](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0028-plugin-credentials-and-pairing.md).

## Secrets in configuration

Declare a secret field in a setup flow, and read it back only when you need it:

```csharp
ActionParameter.Secret("api_key", label: Strings.Setup.ApiKey(), required: true)

// Values the user never typed, such as OAuth tokens:
["access_token"] = ConfigFlowValue.Secret(token.AccessToken),

// At runtime - decrypted only on request:
var apiKey = await context.Config.GetSecretAsync(entry.Id, "api_key");
```

| Rule | Detail |
| --- | --- |
| Stored encrypted | `Secret` and `Password` fields and `ConfigFlowValue.Secret` values go into SQLite through ASP.NET Core Data Protection. `Plain` values are stored as they are |
| Referenced, never inlined | Configuration refers to a secret as `{ "$secret": "<id>" }`, so no flow or action document carries plaintext |
| Key ring protected by the OS | The Data Protection key ring on disk under the host's data root is encrypted under a key in the OS credential store - Windows Credential Manager, macOS keychain, or Secret Service on Linux - so a copy of the data directory does not carry the key that opens it |
| Honest fallback | With no credential store, and in a portable installation, the key ring stays readable on disk and the host reports that state rather than implying protection |
| Never log or show | Secrets never belong in logs or user-visible errors: log the failure type, not the value |
| Decrypted only where declared | A flow's secret reference is decrypted only into a parameter the action declares as `Secret` or `Password`, nested ones included; see [secret resolution in flows](/policies/compatibility/#secret-resolution-in-flows) |
| Approved blocks only | A flow step that uses a secret runs only in the exact form the user approved. Editing in the app approves it; any other change pauses the step as **waiting for approval** until the user approves it under **Approvals** |
| Variables are not part of the approval | An approved step that takes its other values from variables, script inputs, event parameters or an earlier step in the same flow uses whatever those hold when it runs |

See [setup flows](/features/setup-flows/#secrets) and
[ADR 0047](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0047-secrets-backups-and-restore.md).

## MCP clients

An AI client connected to [the MCP server](/reference/mcp/) is treated as an untrusted writer, not as the
user:

- Its token opens only `/mcp`, and never an admin, client or plugin endpoint. `/mcp` is also served on
  the public listener so clients on other computers in the network can connect; the token is the only
  credential there, sent over plain HTTP. Browser pages are refused by the `Origin` checks.
- No tool returns a secret value. Secrets appear as `{"$secret":"<id>"}` references, and a new secret is
  typed by the user into a one-time Macro Deck page, never into the client.
- An action that asks first is confirmed by the user in the client's own dialog or, for a client that can
  only open links, on a one-time Macro Deck confirmation page with the same `Origin`, one-time and
  expiry rules as the secret page.
- Revoking the token in **Settings > AI clients** cuts off its open requests and sessions immediately.

## Signing and install trust

Check an artifact yourself, or in CI, before it reaches a host:

```bash
macrodeck-plugin verify ./artifacts/com.example.hello-deck-1.0.0-linux-x64.macroDeckPlugin
```

### Artifacts

A `.macroDeckPlugin` artifact is a ZIP whose root is the version directory. Before a byte reaches its
destination, the installer enforces:

| Check | Rejects |
| --- | --- |
| Path safety (zip-slip) | Absolute paths, Windows drive letters, `..` segments, any resolved destination outside the target |
| Filesystem traps | Illegal filename characters, reserved Windows device names (`CON`, `PRN`, `COM1`, ...), any symlink, fifo, socket or device node |
| Zip-bomb caps | Entry count, per-entry uncompressed size, total uncompressed size, compressed size, compression ratio - measured **live through a bounded stream**, never taken from the archive's central directory |
| Payload identity | When the manifest declares `files[]`: a file whose SHA-256 or size does not match, and any extracted file *not* declared |
| Identity | `id` or `version` that does not match the directory names they install under |

**Nothing from an artifact is ever executed during install, activate or uninstall.** The manifest has no
hook field; the only process a version directory ever produces is the supervisor's own health-gated launch
after activation. The execute bit is set only on the entrypoints for the current runtime identifier, and a
self-contained entrypoint may not be a shell script.

**Activation is atomic.** `current.json` is written to a sibling temporary file and renamed over the real
one - a single filesystem operation on every supported platform - so it always names the previous version
or the new one. If the new version does not reach a healthy running state in time, the rename is undone,
the failed version is removed, and the previous version is restarted if it was running. See
[ADR 0029](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0029-plugin-packaging-installation-and-supervision.md).

### Signing: the Creator Portal signs, and the host verifies before install and before every load

Verification lives in
[`MacroDeck.Signing`](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/sdk/src/MacroDeck.Signing/README.md),
used by both the host and the `macrodeck-plugin` CLI's [`verify`](/cli/signing/#verify) and
[`keygen`/`sign`](/cli/signing/#keygen) commands.

**When it is checked:** in a staging directory before anything is written into the plugin directory, and
again on every launch of an installed plugin. The plugin directory is user-writable on every platform, so
editing a plugin's files after installation, or stripping the certificate from one installed as trusted,
stops it from loading.

The host resolves a package to exactly one verdict:

| Outcome | Verdict | Blocks install? |
| --- | --- | --- |
| Signature valid, chained to the Macro Deck root directly or through an issuer certificate | `Trusted` | No |
| No `signature` declared | `Unsigned` | Only without consent |
| Signature block malformed | `Malformed` | Yes |
| Signature present but does not verify | `SignatureInvalid` | Yes |
| Contents do not match the signature | `ContentMismatch` | Yes |
| Certificate does not chain to the pinned root | `UntrustedRoot` | Yes |
| Certificate issued for another purpose | `WrongCertificatePurpose` | Yes |
| Certificate not valid at `signedAt` | `CertificateNotValidAtSignature` | Yes |
| Certificate, or the issuer that signed it, revoked by the Store registry | `Revoked` | Yes, at install and update only |
| Package unreadable, or algorithm unknown to this host | `VerificationUnavailable` | Yes |

**`Unsigned` is the only verdict a confirmation can admit**, because nothing published today is signed yet:

| Source of an unsigned plugin | Installs? |
| --- | --- |
| A `.macroDeckPlugin` you select yourself, open through the file association, or upload | After a deliberate confirmation, and stays marked unverified |
| The Store or registry, Developer Mode on | Refused first; installs only if you then confirm the same warning. Developer Mode is read by the host from its own settings, so asking for the exception without it changes nothing |
| The Store or registry, Developer Mode off | No |
| An update to a plugin installed as `Trusted` | No, regardless of consent |
| An update Macro Deck found for you | Only ever reported to you, never installed unsigned on your behalf |

**A plugin id stays with the publisher it was first admitted from.** Once a version of a plugin was admitted
as `Trusted`, a later package for the same plugin id - a higher version, an older one, or a forced reinstall
of the same version - must be signed under a certificate issued to the same subject: the certificate's
`subject.kind` and `subject.id`, the publisher the Creator Portal verified. The certificate id is not
compared, so a publisher whose certificate is rotated or re-issued, or whose certificate comes from a new
issuer, keeps updating. A package that verifies as `Trusted` but was issued to another subject is refused
with `SignerMismatch` (`signer_mismatch` over HTTP), and the installed version stays active.

| Situation | Outcome |
| --- | --- |
| A file you select or upload, or a URL, signed by another publisher | Refused, with or without consent |
| A Store install, update or automatic update signed by another publisher | Refused; the Store marks the operation failed and does not offer a retry |
| A test build signed by another publisher | Refused. Real test builds are unsigned and are not compared |
| An unsigned update over an unsigned plugin | Unchanged: there is no publisher to compare, so the per-install confirmation decides |
| The first signed version over unsigned ones | Installed, and its publisher is compared from then on |

The comparison uses the most recently admitted trusted version of the plugin id. Its record is kept even
after its version folder is pruned, so pruning never lowers the trust tier either: an unsigned package is
still refused as a downgrade. A version admitted before Macro Deck recorded the subject is matched by its
exact certificate id instead, so a rotated certificate over such a version is refused until the plugin is
reinstalled. A Creator Portal [transfer](/creator-portal/projects/#transfer-a-project) of a signed plugin
changes the subject too, so installed copies refuse the new publisher's releases. Uninstalling clears the
comparison; unless you also delete the plugin's
stored data, a package from another publisher installed afterwards receives that data. Verifiers read the
subject from `PackageVerifyResult.Subject` in `MacroDeck.Signing`. See
[ADR 0112](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0112-a-trusted-plugin-id-stays-with-its-publisher.md).

Icon packs and profile templates have no signature of their own and are unaffected. A signature that is
*present but does not verify* is a failure, not a weaker kind of unsigned: no confirmation installs it. An
unknown signature algorithm fails closed rather than being treated more leniently than no signature.

**Signature format.** Every signable format - `.macroDeckPlugin`, `.macroDeckIconPack`, and the portable
`.macroDeckProfile`, `.macroDeckFolder` and `.macroDeckWidget` - carries its signature in its own manifest
and its certificate as `certificate.json` and `certificate.sig` at the archive root, plus `issuer.json` and
`issuer.sig` when an issuer certificate signed that certificate. There is no detached signature file: a
signed artifact verifies on its own. The signature covers a format-specific canonical
digest - the package identity and the declared file list, never the manifest's own JSON encoding - so
reformatting a manifest does not invalidate a signature, while adding a file or repointing an entrypoint
does. See [the manifest's `signature` field](/reference/manifest/#signature) and the
certificate ([v1](/schemas/macrodeck-certificate-v1.schema.json),
[v2](/schemas/macrodeck-certificate-v2.schema.json)) and
[package signature](/schemas/macrodeck-package-signature-v1.schema.json) schemas.

**Trust anchor.** `MacroDeck.Signing.MacroDeckRootKey` (formerly
`MacroDeckHost.Domain.Security.MacroDeckRootKey`, which no longer exists) holds the public half of an
offline Ed25519 key pair, verification-only. The private half never exists on a build machine or in CI, and
is not any of the release-signing keys. `--root-public` on `sign` and `verify` points at a different root
for testing; both commands then warn `non-production-root` and report a result not anchored to the Macro
Deck root.

**Certificate chain.** A certificate is signed either by the root itself or by exactly one **issuer
certificate** the root signed, so the root can stay offline while the Creator Portal issues certificates
with the issuer's key. The rules are strict:

- An issuer certificate carries exactly the `issuer` key usage and the `issuer` subject kind, uses
  `schemaVersion` 2, is signed by the root, and names no issuer of its own. There is never a second
  intermediate level.
- A certificate it signs uses `schemaVersion` 2, names the issuer's `certificateId` in `issuer` and the same
  `rootKeyId`, carries exactly `package` or `registry`, and has a validity window inside the issuer's.
- An issuer certificate never signs a package or a registry manifest, and a `package` or `registry`
  certificate never signs another certificate.
- Both validity windows are evaluated at the signature's `signedAt`.
- A `schemaVersion` 1 certificate is always signed by the root, exactly as before.

The registry publishes an issuer certificate under `certificates/` beside the certificates it signed. Macro
Deck versions from before this chain refuse an issuer-signed certificate. See
[ADR 0096](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0096-offline-root-with-an-online-issuer.md).

**Store artifacts are signed by the Creator Portal, not by their author.** Publishing runs as Trusted
Publishing: the plugin's CI workflow authenticates to the Creator Portal with its own workload identity,
the Portal verifies that the workflow is a trusted publisher and checks the run's provenance, then signs
the artifact server-side. **Plugin developers and their CI workflows do not generate, receive, manage or
hold signing keys, certificates or signing credentials** - no signing secret in a repository or CI secret
store, and no manual artifact upload in the publishing path. Certificate issuance and revocation also live
in the Creator Portal. See [Publishing to the Store](/guides/publishing/) and
[ADR 0042](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0042-plugin-signing-and-trusted-publishing.md).

**The CLI signs and verifies; it never issues trust.** The public CLI has no root key generation, no
certificate issuance and no registry signing, and `keygen`/`sign` is not the Store path - it is for
artifacts distributed outside the Store and for Macro Deck's own infrastructure. `keygen` produces a
creator Ed25519 key pair and nothing else; `sign` requires a certificate only the Creator Portal can issue,
validates the whole artifact against it, and embeds the signature. `verify` checks the certificate chain,
the certificate's validity **at the signature's own `signedAt`** rather than at verify time, the canonical
digest, and every declared file's hash and size. See [`sign`](/cli/signing/#sign) and
[`verify`](/cli/signing/#verify).

| Limit | What it means |
| --- | --- |
| `verify` never consults revocation | It says so on every run, in both output formats. A `valid` verdict is a fact about the signature and chain at signing time, not a live trust decision |
| Revocation stops new installs, not installed plugins | The revoked keys come from the `security.json` of the signed Store registry the host has loaded. A package whose certificate, or whose certificate's issuer, is listed there is refused at install and update. A version that is already installed keeps activating and launching, and the Store marks it **Certificate revoked**. See [ADR 0096](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0096-offline-root-with-an-online-issuer.md) |
| Revocation needs a loaded registry | A host that has not loaded a registry snapshot yet, or never can because it is offline, answers `Unavailable`, and that deliberately does not block: failing closed would refuse every signed plugin. See [ADR 0044](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0044-plugin-and-store-trust-enforcement.md) |
| A leaked signing key must be contained by other means | Treat everything signed with it as untrusted. For the Store that is a Creator Portal concern, since the keys are the Portal's; outside the Store see [private-key handling](/cli/signing/#private-key-handling) |
| `signedAt` is not authenticated | No canonical digest covers it, so the key holder can set any value. Checking validity at `signedAt` is advisory against that key holder: it only protects a package from its certificate's later expiry. Revocation is the control that stops new installs signed with a compromised or misused key |
| `publisher` is a claim | It becomes an attribution only behind a `Trusted` verdict. Verified publisher identity comes from the Creator Portal having checked the workflow's provenance before signing, never from the manifest |
| Only plugins carry a publisher signature | Store icon packs and profile templates are authenticated by the signed registry: the pinned root signs the registry's certificate, directly or through an issuer certificate, that certificate signs the registry manifest, and the manifest carries the digest and size of every file, including the release manifest that declares each artifact's digest. That proves the bytes are the ones the Macro Deck registry published, nothing more: they are never presented as publisher-verified, and a publisher's revoked certificate does not apply to them |

<a id="permissions-declared-not-enforced"></a>

## Permissions: declared, and enforced only for ADB, Companion devices and desktop notifications

```json
{ "permissions": ["host:variables", "host:adb"] }
```

A manifest may declare permissions from a fixed vocabulary (`host:variables` through `device:usb`),
mirroring the host callback surface one for one. They are parsed, shape-validated, persisted and surfaced
through the installed-plugins API so a consent surface has something real to render.

**The host gates exactly three host APIs on a declared permission: `adb`, on `host:adb`,
`companion-devices`, on `host:companion-devices`, and `desktop-notifications`, on
`host:desktop-notifications`.** Every other host callback is served whether or not the calling plugin declared the permission that covers it. Plugins that
predate the vocabulary declare nothing, so a default-deny posture for the rest would break every plugin
already running, and a default-allow posture would not be a boundary worth the name.

All three are enforced because they were new: no existing plugin relied on them. A plugin reaches Macro Deck's
ADB connection only when ADB is enabled, the user has left **Allow plugins to use ADB** on, and the active
version of the installed plugin declares `host:adb`. Installing a plugin that declares it while ADB or
that setting is off asks the user whether to turn both on. A self-registered session, admitted through a
developer token or interactive pairing, is exempt from the declaration because the user admitted it by
hand. See [Android devices](/features/android-devices/) and
[ADR 0092](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0092-plugins-reach-adb-through-a-permission-gated-host-api.md).

**This is user control, not isolation.** A plugin process is not sandboxed and could run its own `adb`
without declaring anything. `host:adb` gives the user a visible, switchable decision about plugins using
Macro Deck's connection; it does not stop a hostile plugin from reaching a device. Local paths an `adb`
call names are read and written with Macro Deck's identity, which is the same user the plugin already runs
as.

**A locked host runs only fixed ADB commands.** While Macro Deck is locked, the open-ended operations (shell,
push, pull, install, uninstall, connect) are refused for plugins and built-in integrations alike. The typed
operations (keys, taps, swipes, text, starting and stopping an app, opening a URI, reboot, screenshot, screen
status) still run, so a deck button bound to one keeps working; each is a fixed command whose arguments the
host validates and quotes. A plugin allowed to use ADB can therefore, for example, type text or capture the
screen of an attached device while the computer is locked. On Linux the host cannot detect the lock and is
never in the locked state, so none of these operations are refused there and deck actions keep running on a
locked computer.

<a id="companion-devices"></a>

**Companion devices are off until the user allows them.** A plugin reaches the phones and tablets
connected through the Companion app only when the user has turned on **Allow plugins to use Companion
devices** in **Settings > Companion App**, which is off by default, and the active version of the installed
plugin declares `host:companion-devices`. Installing a plugin that declares it while that setting is off
asks the user whether to turn it on; a plugin that declares it and is refused a call asks once per run of
Macro Deck. A self-registered session counts as declaring it, as for `host:adb`; the setting still applies.
An allowed plugin reads everything the devices report, including battery, model, network type and the
Wi-Fi network's name, sends them commands (brightness, orientation, vibration, screen on and off, bringing
Macro Deck to the front) and captures screenshots. A deck screenshot shows whatever the deck shows,
including other plugins' widgets, and needs no approval on the device; a full screen screenshot still needs
the device's user to approve it on the device, whatever the setting says. The setting covers every plugin
that declares the permission, and Macro Deck's own integrations are not subject to it. This too is user
control rather than isolation: the plugin process itself stays unsandboxed. See
[Companion devices](/features/companion-devices/) and
[ADR 0110](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0110-plugins-reach-companion-devices-through-a-permission-gated-host-api.md).

<a id="desktop-notifications"></a>

**Desktop notifications need only the declaration.** A plugin shows a notification from the operating
system, attributed to Macro Deck, only when the active version of the installed plugin declares
`host:desktop-notifications`; a self-registered session counts as declaring it. There is no separate
setting: such a notification carries only the text the plugin chose, and the operating system's own
notification settings for Macro Deck still apply. A plugin could show its own notifications without Macro
Deck, so this too is user control rather than isolation. See
[Desktop notifications](/features/notifications/#desktop-notifications).

Treat the other declared permissions as documentation of intent, not as a constraint on what a plugin can
reach.

## Messages between plugins

Plugins and integrations can exchange messages through Macro Deck's
[message channel](/features/messaging/). Macro Deck stamps every message with the sender's integration
id, so `Sender` is reliable, but it authorizes nothing: any plugin can publish on any topic, send commands
and requests to any handled topic, and handle any topic nobody has claimed yet. Treat a message like any
other input from another process: validate its payload, and check `Sender` before acting on a command
where it matters who asked. `host:messaging` is declared, not enforced.

## Colour variables

Any plugin can resolve the current value of any Color variable through
[`IColorApi`](/features/variables/#resolving-colours-yourself): every global one, and the widget-scoped ones
of any existing widget, whichever plugin or user created them. It can also watch them for changes. That is
all the `colors` host api reveals: a variable of any other type, a missing one or an unknown widget
resolves to no colour, and the api accepts only a colour or a Color variable reference, never an arbitrary
template, so it cannot be used to read text, numbers or other values. No permission covers it.

## Video streams

A [video stream provider](/features/video-streams/) gives Macro Deck the URL of a stream, and Macro Deck
relays the media to every client signed in to Macro Deck, the desktop app or any paired device. The
provider's URL never reaches a client, so credentials in it stay on the computer; a provider still must
not serve anything it would not show every signed-in client, and never puts the source's own password or
API key in a URL. Macro Deck never logs a description. `host:video-streams` is declared, not enforced.

The relay is a deliberate, bounded fetch on behalf of a plugin:

- **The relay URL is a capability.** `/api/video-streams/relay/<token>/...` carries an unguessable token that
  is bound to one session, sent only over the authenticated UI WebSocket to the connection that owns the
  session, and dead once the session ends. The route is anonymous by design, because an image or video
  element cannot send an authorization header. Anyone who holds the URL can read that one stream until it
  ends, which is no more than the client it was sent to could. Macro Deck redacts the token from its logs.
- **It pins the origin of the provider's URL and nothing else.** Redirects and HLS playlist URIs stay on that
  origin (apart from `data:` and `skd:` URIs, which are passed on untouched), and only `GET` and `HEAD` are sent. The provider chooses the origin, so a plugin can point the
  relay at any service on the computer or the local network, or at Macro Deck itself. A plugin could make
  those requests without the relay, so this adds no capability, but the relay returns the response to a
  client, which is why only responses that look like the session's media are passed on.
- **Plugin bytes cannot run as a page.** Content types are checked against the session's transport,
  responses carry `X-Content-Type-Options: nosniff` and a sandboxing Content-Security-Policy, and a
  response that does not fit is refused, so a plugin cannot serve a script or an HTML page from Macro
  Deck's own origin.
- **It is bounded.** Eight concurrent relayed requests per session, 64 in all, a header timeout and an
  idle timeout on media.

A tree can name any provider's stream in a [`macrodeck.video-stream`](/ui/components/video-stream/), so
any plugin's view can make the clients that draw it open sessions on another plugin's provider. That is no
wider than what a signed-in client can already open, but it means a stream's audience is everyone who can
see any Macro Deck view, not only the views its own plugin draws.

## Links opened on a client

A view that a user pressed can ask Macro Deck to open a web page in the browser of the device that pressed,
through [`IUiEventOrigin.OpenUrlAsync`](/ui/views/sessions/#opening-a-link-on-the-client-that-pressed) or the
`ui`/`origin.open-url` call. Every plugin and integration that serves a view can do this, and no permission
covers it. Opening a page on someone's phone is visible and can be abused, so the call is narrow:

- **Web pages only.** Macro Deck accepts only an absolute `http` or `https` URL, and checks it again before
  sending it; the web client checks it a third time before it reaches the browser. A `javascript:`, `data:`,
  `file:` or custom-scheme link is refused, so a plugin cannot run script in a client's page or hand a
  device's operating system a file or an app link.
- **Only in answer to a press, on the device that pressed.** The link goes only to the connection that sent an
  event to that session in the last 10 seconds and is still attached to it, never to another tab or device
  that shares its client id and never to a client that is merely showing the view. A plugin cannot open
  pages on a deck nobody is touching.
- **Only on clients that can.** A client must declare that it opens links when it connects. Hardware decks
  and older clients never receive one.
- **One link per press.** Each event lets the view open one page; a second call for the same press is
  refused. Calls from a plugin also count against its callback throttle like any other call that puts
  something on a user's screen.
- **No way back.** The web client opens the page in a new tab without a reference to the deck's page, and
  the deck's page sends no `Referer`, so the opened site learns neither the address of Macro Deck on the
  local network nor anything else about the deck. When the browser blocks the new tab, the web client shows
  the link to tap instead of opening it.

The desktop UI runs on the computer Macro Deck runs on, so a press there opens the page in that computer's
browser. What the page itself does is up to the site, as with any link the user follows.

## Battery devices

Every plugin can read every [battery device](/features/batteries/) in Macro Deck: the ones other plugins
publish, Macro Deck's own, the Android phones and tablets its ADB integration reports, the devices running
Macro Deck Companion, the computer's own battery under the computer's name, and the Home Assistant battery
sensors the user picked, under their device name. The host pushes the whole catalog, each device's name,
kind, level, charge state and connection, to every connected plugin. No permission covers publishing or reading: the ADB devices are not
filtered by **Allow plugins to use ADB** or `host:adb`, and the Companion devices not by **Allow plugins to
use Companion devices** or `host:companion-devices`. A battery level is not access to the device, and
gating it would leave other plugins' widgets blank. This is a deliberate decision, recorded in
[ADR 0109](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0109-battery-devices-are-a-host-brokered-catalog-every-plugin-may-read.md).

- **Ids carry no hardware identifiers.** The ADB integration publishes a phone under an HMAC of its
  serial, keyed with a secret of this install, so the serial never leaves Macro Deck's ADB service, is
  never stored, and cannot be matched against a list of serials or across installs. The Companion
  integration publishes a device under a hash of its configuration id, so the Companion device id, which
  only plugins with `host:companion-devices` see, is not handed out either. A provider must not put a
  serial or anything else it would not show every plugin into a device's name or id.
- **A provider speaks only for itself.** Devices are stored under the authenticated plugin or the
  integration that published them; a plugin cannot publish, change or remove another provider's devices.
- **Bounded.** A provider holds at most 64 devices, and a publish that fails validation changes nothing.
- **Little is kept.** Devices live in memory only. What persists is a Battery widget's configuration (the
  device's qualified id and last name) and the battery variables.

## Logging and redaction

```csharp
_logger.Information("Authenticated as {User}", account.DisplayName); // not the token
```

The host redacts every event once, before any sink, to `***`: values of sensitive keys (`password`,
`token`, `secret`, `api_key`, `authorization`, `client_secret` and similar) in `key=value` or
`"key": value` form, credentials in URLs (`https://user:pass@`), `Bearer`/`Basic`/`Digest` credentials,
JWTs and PEM private keys. That is a safety net, not a licence: a secret in an unrecognised shape is written
as-is into a file users attach to bug reports. Plugin identity on a log line comes from the authenticated
session, so a plugin cannot log under another integration's name. See
[logging](/features/logging/#never-log-secrets).

## Threat-model limitations

Read this before deciding what a plugin should be trusted with.

| Limitation | Detail |
| --- | --- |
| A plugin runs with the user's full privileges | A managed plugin is an ordinary child process: no sandbox, container, separate account or privilege reduction. It can do anything the user can - read and write their files, open network connections, start processes. Installing one is equivalent to running any other downloaded program |
| Permissions are not a boundary | A plugin that declares nothing can still reach every host API except `adb`, `companion-devices` and `desktop-notifications`, and even these gate only Macro Deck's own connections and notifications, not a plugin's own - see [above](#permissions-declared-not-enforced) |
| An unsigned plugin is still admitted on your say-so | See [signing](#signing-the-creator-portal-signs-and-the-host-verifies-before-install-and-before-every-load). For an unsigned install the declared-digest check is corruption detection, not a boundary: whoever can rewrite the binary can rewrite the unsigned manifest. Trust rests on where you got the file |
| Revocation stops new installs only | A plugin installed before its certificate or issuer was revoked keeps running; the Store marks it, and removing it is your decision |
| A process running as the same user can become admin | It can read the desktop app's loopback secret. The model defends against LAN callers, browsers and other local accounts, not a hostile process running as the same user |
| The plugin secret and session token cross plain HTTP on loopback | The local-only rule confines that to processes already on the machine, but it is not encryption |
| A self-signed public certificate proves nothing about identity | It encrypts the link; it does not authenticate the host to a stranger, and a DHCP change means regenerating it |
| Supervision contains failure, not intent | Isolation is not a goal. The child's environment is scrubbed of inherited `MACRO_DECK_PLUGIN_*` and `ASPNETCORE_URLS`; the listener port is bound by the host and handed to the child; a fresh credential is minted per launch and discarded on exit; a restart budget stops a crash loop. See [ADR 0029](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0029-plugin-packaging-installation-and-supervision.md) |
| A hard kill is a hard kill | A plugin that does not exit within its graceful timeout has its process tree killed. An SDK that does not recognise close code `4004` gets no notice and no chance to flush ("kill-only" in ADR 0029) |
| Imported fonts are parsed as untrusted input | A `.macroDeckProfile`, `.macroDeckFolder` or `.macroDeckWidget` archive, including a Store profile template, can carry TTF or OTF fonts. The host checks each font's bytes against its recorded SHA-256, accepts only single-face TTF/OTF files up to 32 MB that it can parse, and never lets an imported font replace or extend a family installed on the computer, so an archive cannot change how installed fonts or the app font render. The font bytes are still parsed by the host's font engine and by every client's renderer |
| macOS arm64 and unsigned binaries | An unsigned, freshly extracted Mach-O needs at least an ad-hoc signature on Apple silicon before the kernel runs it, and the installer does not add one. This is a known, unsolved gap. |

## Reporting a vulnerability

Report privately through GitHub's
[private vulnerability reporting](https://github.com/Macro-Deck-App/Macro-Deck/security/advisories/new),
never a public issue, discussion or Discord. What to include, supported versions and scope are in
[SECURITY.md](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/SECURITY.md).

| The flaw is in | Report to |
| --- | --- |
| Macro Deck: host, first-party clients, built-in integrations, SDK, plugin infrastructure | This repository, privately |
| A third-party plugin that exploits Macro Deck or bypasses a boundary Macro Deck should enforce | This repository, privately |
| Only a third-party plugin | Its author, or its repository's security process. Store plugin with no suitable contact: report it through the Store |

## See also

- [Authentication](/reference/authentication/) - credentials, session exchange, and what a plugin must
  never do with either.
- [Plugin hosting](/reference/plugin-hosting/) - the artifact format, the installer's rejection rules, and
  what the supervisor injects.
- [Compatibility policy](/policies/compatibility/) - what is frozen and what may change.
