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
| Desktop app, over the private loopback port | **Admin**, with no token | The same trust goes to any local process - see [loopback trust](#loopback-trust-is-transport-trust-not-authentication) |
| Plugin process | Scope `plugin`: the plugin protocol surface, and nothing else | Not sandboxed: it runs with the user's full privileges. Declared permissions are [not enforced](#permissions-declared-not-enforced) |
| Deck clients (web client, companion) | Scope `client`: the viewer-safe endpoints | Plugin endpoints refuse them |
| LAN callers and browsers | The public listener's API and web client | Every plugin endpoint refuses them |
| Creator Portal and Store | Signing Store plugins after checking the publishing workflow's provenance; publishing the signed registry | Revocation: no feed is published yet |

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

### Loopback trust is transport trust, not authentication

A request on the *private* loopback port, from a loopback address, with a loopback `Host` header, is
authenticated as **admin**. That is how the desktop UI works without handling a token.

[ADR 0003](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0003-loopback-trust-token-scopes-and-device-identity.md)
accepts the limit: **any local process can already reach the loopback port**. A hostile process running as
the same user on the same machine is outside what this boundary defends against; the ADR rejects a
bootstrap token because it would carry "the same local-attacker exposure". The `Host` header check is a
DNS-rebinding guard, not a second authentication factor.

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
- **Client sessions** (web client, companion) hold a 15-minute access token and a refresh token that
  rotates on every use and lives 365 days. A rotated refresh token presented again within 30 days of its
  rotation revokes every session of the account. The companion pairs with a six-digit, single-use code
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
Pairing does not widen that existing exposure.

What pairing changes is what a hostile local process needs. Before, it could mint a Developer token itself
through its implicit loopback admin. Now it needs a human to approve a specific prompt while Developer Mode
is on. That is bounded by Developer Mode being off by default, approval only over the trusted transport, a
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

See [setup flows](/features/setup-flows/#secrets) and
[ADR 0047](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0047-secrets-backups-and-restore.md).

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
| Signature valid, chained to the Macro Deck root | `Trusted` | No |
| No `signature` declared | `Unsigned` | Only without consent |
| Signature block malformed | `Malformed` | Yes |
| Signature present but does not verify | `SignatureInvalid` | Yes |
| Contents do not match the signature | `ContentMismatch` | Yes |
| Certificate does not chain to the pinned root | `UntrustedRoot` | Yes |
| Certificate issued for another purpose | `WrongCertificatePurpose` | Yes |
| Certificate not valid at `signedAt` | `CertificateNotValidAtSignature` | Yes |
| Certificate revoked | `Revoked` | Yes |
| Package unreadable, or algorithm unknown to this host | `VerificationUnavailable` | Yes |

**`Unsigned` is the only verdict a confirmation can admit**, because nothing published today is signed yet:

| Source of an unsigned plugin | Installs? |
| --- | --- |
| A `.macroDeckPlugin` you select yourself, open through the file association, or upload | After a deliberate confirmation, and stays marked unverified |
| The Store or registry, Developer Mode on | Refused first; installs only if you then confirm the same warning. Developer Mode is read by the host from its own settings, so asking for the exception without it changes nothing |
| The Store or registry, Developer Mode off | No |
| An update to a plugin installed as `Trusted` | No, regardless of consent |
| An update Macro Deck found for you | Only ever reported to you, never installed unsigned on your behalf |

Icon packs and profile templates have no signature of their own and are unaffected. A signature that is
*present but does not verify* is a failure, not a weaker kind of unsigned: no confirmation installs it. An
unknown signature algorithm fails closed rather than being treated more leniently than no signature.

**Signature format.** Every signable format - `.macroDeckPlugin`, `.macroDeckIconPack`, and the portable
`.macroDeckProfile`, `.macroDeckFolder` and `.macroDeckWidget` - carries its signature in its own manifest
and its certificate as `certificate.json` and `certificate.sig` at the archive root. There is no detached
signature file: a signed artifact verifies on its own. The signature covers a format-specific canonical
digest - the package identity and the declared file list, never the manifest's own JSON encoding - so
reformatting a manifest does not invalidate a signature, while adding a file or repointing an entrypoint
does. See [the manifest's `signature` field](/reference/manifest/#signature) and the
[certificate](/schemas/macrodeck-certificate-v1.schema.json) and
[package signature](/schemas/macrodeck-package-signature-v1.schema.json) schemas.

**Trust anchor.** `MacroDeck.Signing.MacroDeckRootKey` (formerly
`MacroDeckHost.Domain.Security.MacroDeckRootKey`, which no longer exists) holds the public half of an
offline Ed25519 key pair, verification-only. The private half never exists on a build machine or in CI, and
is not any of the release-signing keys. `--root-public` on `sign` and `verify` points at a different root
for testing; both commands then warn `non-production-root` and report a result not anchored to the Macro
Deck root.

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
| Revocation is enforced but never fed | A revoked certificate refuses install and launch, but Macro Deck publishes no revocation feed, so nothing is revoked in practice. The host's shipped revocation source always answers `Unavailable`, and that deliberately does not block - failing closed on a feed that does not exist would refuse every signed plugin. See [ADR 0044](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0044-plugin-and-store-trust-enforcement.md) |
| A leaked signing key must be contained by other means | Treat everything signed with it as untrusted. For the Store that is a Creator Portal concern, since the keys are the Portal's; outside the Store see [private-key handling](/cli/signing/#private-key-handling) |
| `signedAt` is not authenticated | No canonical digest covers it, so the key holder can set any value. Checking validity at `signedAt` is advisory against that key holder: it only protects a package from its certificate's later expiry. Revocation, once it exists, is the control that stops a compromised or misused key |
| `publisher` is a claim | It becomes an attribution only behind a `Trusted` verdict. Verified publisher identity comes from the Creator Portal having checked the workflow's provenance before signing, never from the manifest |
| Only plugins carry a publisher signature | Store icon packs and profile templates are authenticated by the signed registry: the pinned root signs the registry's certificate, that certificate signs the registry manifest, and the manifest carries the digest and size of every file, including the release manifest that declares each artifact's digest. That proves the bytes are the ones the Macro Deck registry published, nothing more: they are never presented as publisher-verified |

## Permissions: declared, not enforced

```json
{ "permissions": ["host:variables"] }
```

A manifest may declare permissions from a fixed vocabulary (`host:variables` through `device:usb`),
mirroring the host callback surface one for one. They are parsed, shape-validated, persisted and surfaced
through the installed-plugins API so a consent surface has something real to render.

**Nothing in the host gates a host callback on whether the calling plugin declared the permission that
covers it.** ADR 0029 calls this a sequencing decision, not an oversight: every plugin today declares no
permissions, and self-registering development plugins have no manifest, so "a default-deny posture would
break every plugin already running, and a default-allow posture would not be a security boundary in any
sense worth calling one." Enforcement at the host callback router is a named follow-up that needs its own
ADR.

Treat declared permissions as documentation of intent, not as a constraint on what a plugin can reach.

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
| Permissions are not a boundary | A plugin that declares nothing can still reach every host API - see [above](#permissions-declared-not-enforced) |
| An unsigned plugin is still admitted on your say-so | See [signing](#signing-the-creator-portal-signs-and-the-host-verifies-before-install-and-before-every-load). For an unsigned install the declared-digest check is corruption detection, not a boundary: whoever can rewrite the binary can rewrite the unsigned manifest. Trust rests on where you got the file |
| Revocation is enforced but never fed | A compromised signing key has to be contained by other means until a feed ships |
| Any local process can reach the loopback listener | And is trusted as admin on the private port. The model defends against LAN callers and browsers, not a hostile process running as the same user |
| The plugin secret and session token cross plain HTTP on loopback | The local-only rule confines that to processes already on the machine, but it is not encryption |
| A self-signed public certificate proves nothing about identity | It encrypts the link; it does not authenticate the host to a stranger, and a DHCP change means regenerating it |
| Supervision contains failure, not intent | Isolation is not a goal. The child's environment is scrubbed of inherited `MACRO_DECK_PLUGIN_*` and `ASPNETCORE_URLS`; the listener port is bound by the host and handed to the child; a fresh credential is minted per launch and discarded on exit; a restart budget stops a crash loop. See [ADR 0029](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0029-plugin-packaging-installation-and-supervision.md) |
| A hard kill is a hard kill | A plugin that does not exit within its graceful timeout has its process tree killed. An SDK that does not recognise close code `4004` gets no notice and no chance to flush ("kill-only" in ADR 0029) |
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
