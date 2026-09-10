---
title: Security model
description: The trust boundary around a Macro Deck plugin, what the host does and does not guarantee, how credentials and artifacts are handled, and the limits of the model.
---

This page describes what the host actually enforces around a plugin, and - as plainly as possible -
what it does not. It is written to be useful when you are deciding whether to install someone else's
plugin, or reasoning about what your own plugin can be trusted with. Where something is declared but
not enforced, or shipped but not implemented, that is stated rather than glossed.

## The trust boundary

The host runs more than one listener.

| Listener | Reachable from | Notes |
| --- | --- | --- |
| Public listener | The LAN | Serves the API and the web client. HTTPS is optional and configurable - see [ADR 0040](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0040-public-listeners-and-tls.md). While its plain-HTTP port is serving, the host announces its instance name, version and that port on the LAN over mDNS (`_macrodeck._tcp`), unless this is turned off in Settings > Network - see [ADR 0082](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0082-lan-discovery-uses-the-platform-responder.md). |
| Private loopback listener | The local machine only | Never gets TLS: the desktop shell and the plugin SDK reach it over plain HTTP on `127.0.0.1`. |

**Plugin endpoints are served on both listeners but only accept a loopback remote address.** A caller
on the LAN is refused regardless of which port it used, and a request that looks like it came from a
browser (a cross-site `Origin` or `Sec-Fetch-Site`) is refused too. Plugin paths are excluded from the
app-wide CORS policy. The practical consequence is stated in
[ADR 0028](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0028-plugin-credentials-and-pairing.md):
a self-registering plugin must run on the same machine as the host.

Enabling HTTPS on the public listener does not widen this. The local-only gate on plugin endpoints
grants no principal of its own, so reaching it from another listener adds reachability, not authority.

### Loopback trust is transport trust, not authentication

A request arriving on the *private* loopback port, from a loopback address, with a loopback `Host`
header, is authenticated as **admin**. That is how the desktop UI works without ever handling a token.

[ADR 0003](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0003-loopback-trust-token-scopes-and-device-identity.md)
is explicit about the limit this accepts: **any local process can already reach the loopback port**. A
hostile process running as the same user on the same machine is outside what this boundary defends
against, and the ADR rejects a bootstrap-token alternative precisely because it would carry "the same
local-attacker exposure" for more moving parts. The `Host` header check is a DNS-rebinding guard, not a
second authentication factor.

Plain HTTP on the LAN leaves tokens visible to on-path attackers, which is why the public listener can
be configured for TLS. Even then, the shipped posture is a self-signed certificate: it "exists to
encrypt a link the user already trusts, not to prove identity to strangers".

### Scopes

Authorisation is deny-by-default: an endpoint with no authorisation metadata is admin-only.

| Scope | Grants |
| --- | --- |
| `admin` | Everything, including the plugin installation surface |
| `client` | The viewer-safe endpoints a deck client needs |
| `plugin` | The plugin protocol surface, and nothing else |

`plugin` is a peer scope, not a subset. A plugin session token does not grant `client` access to the
deck-viewing endpoints, and neither an admin token nor a client token is accepted where a plugin token
is required. Developer (enrollment) tokens carry the separate scope `plugin:enroll` - the only one
defined today.

## Credentials

The mechanics are in [the authentication guide](/sdk/authentication/); what matters here is how
they are stored and revoked.

- **Launch bootstrap tokens** (managed plugins) live in memory only, keyed by hash, minted per launch
  and discarded on every exit path. A process that outlives its own termination sequence can never
  present a token the host still recognises.
- **Developer tokens and per-plugin secrets** are persisted in the host's SQLite database as SHA-256
  hashes; the plaintext of each is shown exactly once. Verification is constant-time. A plain hash is
  used deliberately: both are high-entropy random values, unlike user passwords, which use PBKDF2.
- **Revocation cascades.** Revoking a Developer token revokes every registration derived from it and
  terminates those sessions immediately. Revoking one registration terminates that plugin's session.
  Expiry is not what makes this immediate: the host re-checks the live session registry on every
  request, so a terminated session stops working at once even though its 15-minute JWT is still
  cryptographically valid.
- **A self-registering plugin's own copy of its secret is not encrypted at rest.** The SDK writes it
  owner-only, to a temporary file made owner-only before anything is written to it, then moves it into
  place. On Windows the per-user profile ACL is the protection.

Integration and widget secrets - the values a config flow collects - are a separate mechanism: stored
encrypted in SQLite through ASP.NET Core Data Protection, with the key ring on disk under the host's
data root, and referenced from configuration as `{ "$secret": "<id>" }` so no flow or action document
ever carries plaintext. That key ring is itself encrypted under a key kept in the operating system's
own credential store - the Windows Credential Manager, the macOS keychain, or the Secret Service on
Linux - so a copy of the data directory does not carry the key that opens it. Where no credential
store is available, and in a portable installation, the key ring stays readable on disk and the host
reports that state rather than implying protection it does not have. See
[ADR 0047](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0047-secrets-backups-and-restore.md).

## Interactive pairing

By default, a self-registering plugin obtains its per-plugin secret through interactive pairing rather
than a Developer token. The full mechanics are in
[the authentication guide](/sdk/authentication/#self-registering-interactive-pairing); this section
is the trust model.

- **Loopback only.** The pairing endpoints are ordinary plugin endpoints: served on both listeners, but
  reachable only from a loopback remote address, same as the rest of the plugin protocol.
- **Gated by Developer Mode, continuously.** Developer Mode is a host setting, off by default. It is not
  just a creation-time check: turning it off invalidates redemption of a request that was already
  approved, so there is no window in which a stale approval survives Developer Mode being switched off.
- **Approval happens only in the trusted desktop app.** The prompt is rendered over the private loopback
  transport the desktop UI already relies on - see [ADR 0003](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0003-loopback-trust-token-scopes-and-device-identity.md).
  An admin bearer token presented over the network listener is deliberately **not** sufficient to approve
  a request; approval is a human act in a specific UI, not an API call any admin-scoped caller could make.
- **Proof of possession, not a shared secret.** The plugin proves it is the process that created the
  request by presenting a PKCE-style verifier at redemption; the host stores only the SHA-256 challenge,
  never the verifier itself. `requestId` is unguessable but not a credential - it authorizes nothing on
  its own, which is why it is safe to place in a URL.
- **One-time, short-lived, one per plugin id, capped, and rate-limited.** A request expires on a
  host-advertised timer, is consumed permanently on successful redemption, and cannot coexist with
  another live request for the same plugin id - a second create is refused with `429` rather than
  silently replacing the request a developer may already be looking at. A global cap on pending requests
  and separate rate limits on creation and redemption bound the total exposure.
- **Nothing persists past a host restart.** Pairing requests are memory-only in every state - pending,
  approved, rejected. A restart discards all of them.
- **Self-reported fields are shown as unverified; transport origin is verified.** The prompt displays the
  requesting process's declared executable path, process id, SDK version and display name exactly as
  reported, labelled unverified, because deriving them from the loopback socket would not actually
  authenticate anything. What the host does verify and shows as verified is whether the request arrived
  on the public listener rather than the loopback listener.
- **Replacement is atomic at redemption.** If the host already holds a registration for the plugin id but
  the local credential is gone, the prompt offers - with a separate confirmation - to replace it.
  Redemption rotates the secret in place and terminates the plugin's live sessions in the same step, so
  there is never an instant where both the old and new secret authenticate. The secret is minted at
  redemption, never at approval: an approval that is never redeemed leaves any existing working credential
  untouched.

### Accepted residual exposure

Pairing endpoints are gated by the same loopback check as the rest of the plugin protocol, which has no
port clause: a device reaching the loopback address through an `adb reverse` tunnel is indistinguishable
at the socket level from an ordinary local process (see
[ADR 0030](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0030-android-usb-connections-over-adb.md)).
This is the same accepted exposure the rest of the plugin protocol already carries; pairing does not
widen it.

What pairing changes is the shape of what a hostile local process needs. Previously, obtaining a plugin
credential required a Developer token - and a local process already has implicit admin over the loopback
transport, so it could mint one itself. Now the same process instead needs a human to approve a specific
prompt while Developer Mode happens to be on. That dependency on user attention is bounded by Developer
Mode being off by default, approval only over the trusted transport, a prompt that names what is being
approved and labels unverified fields as such, the one-request-per-plugin-id and rate-limit rules above,
and nothing being minted before the requester proves possession of the verifier - but it is a bound, not a
cryptographic guarantee, and it is recorded honestly as such in
[ADR 0028](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0028-plugin-credentials-and-pairing.md).

## Artifacts

A `.macroDeckPlugin` artifact is a ZIP whose root is the version directory. What the installer
enforces, before a byte reaches its destination:

- Path safety: absolute paths, Windows drive letters, `..` segments, and any resolved destination
  outside the target are rejected - the classic zip-slip attack.
- Filesystem traps: illegal filename characters, reserved Windows device names (`CON`, `PRN`, `COM1`,
  …), and any symlink, fifo, socket or device node are rejected.
- Zip-bomb caps: entry count, per-entry uncompressed size, total uncompressed size, compressed size and
  compression ratio. These are measured **live while copying through a bounded stream**, never taken
  from what the archive's own central directory claims about itself.
- Payload identity: when the manifest declares `files[]`, every declared file's SHA-256 and size are
  checked against the real bytes, and any extracted file *not* declared is rejected too.
- Identity: `id` and `version` must match the directory names they install under.

**Nothing from an artifact is ever executed during install, activate or uninstall.** There is no hook
field anywhere in the manifest; the only process a version directory ever produces is the supervisor's
own health-gated launch after activation completed. The execute bit is set only on the entrypoints for
the current runtime identifier, and a self-contained entrypoint may not be a shell script.

**Activation is atomic.** `current.json` is written to a sibling temporary file and renamed over the
real one - a single filesystem operation on every supported platform. At every instant that file names
either the previous version or the new one, never a half-written third thing. If the newly activated
version does not reach a healthy running state in time, the rename is undone, the failed version is
removed, and the previous version is restarted if it had been running. See
[ADR 0029](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0029-plugin-packaging-installation-and-supervision.md).

### Signing: the Creator Portal signs, and the host verifies before install and before every load

Signing and local cryptographic verification live in
[`MacroDeck.Signing`](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/sdk/src/MacroDeck.Signing/README.md)
and in the `macrodeck-plugin` CLI's [`verify`](/cli/signing/#verify) command and its
[`keygen`/`sign`](/cli/signing/#keygen) counterparts. The host uses that same library rather than a
verifier of its own.

**A package is verified in a staging directory before anything is written into the plugin directory**, and
verified again on every launch of an installed plugin. The second check is what makes the first mean
anything: the plugin directory is writable by the user on every supported platform, so a package that was
verified at install and modified afterwards would otherwise load unchecked. Editing a plugin's files after
installation, or stripping the certificate from one that was installed as trusted, stops it from loading.

Verification produces an explicit outcome rather than a yes/no - unsigned, invalid signature, contents that
do not match the signature, an untrusted or wrong-purpose certificate, a certificate that was not valid when
the package was signed, a revoked certificate, or a package this host could not check. Everything except
"unsigned" refuses the install outright.

**An unsigned package can still be installed, for now, if you say so explicitly.** Because nothing published
today is signed yet, an unsigned `.macroDeckPlugin` you select yourself - or open through the file
association - installs after a deliberate confirmation, and stays marked as unverified afterwards. An
unsigned plugin from the store gets the same exception only while Developer Mode is on: the store install
is refused first, and installs after that refusal only if you confirm the same warning. Developer Mode is
read by the host from its own settings, so asking for the exception without it changes nothing. Icon packs
and profile templates have no signature of their own and are unaffected.

A package whose signature is *present but does not verify* gets no exception anywhere: that is a failure,
not a weaker kind of unsigned, and no confirmation will install it. Once a plugin has been installed as
signed, an unsigned update to it is refused as well, and an update Macro Deck found for you is only ever
reported to you - it is never installed unsigned on your behalf.

Revocation is enforced where it applies - a revoked signing certificate refuses the install and the launch -
but Macro Deck does not publish a revocation feed yet, so in practice nothing is revoked today. See
[ADR 0044](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0044-plugin-and-store-trust-enforcement.md).

Every signable format - `.macroDeckPlugin`, `.macroDeckIconPack`, and the portable `.macroDeckProfile`,
`.macroDeckFolder` and `.macroDeckWidget` - carries its signature inside its own manifest and its
certificate as `certificate.json` and `certificate.sig` at the archive root. There is no detached
signature file: a signed artifact verifies entirely on its own. The signature covers a format-specific
canonical digest - the package identity and the declared file list, never the manifest's own JSON
encoding - so reformatting a manifest does not invalidate a signature while adding a file or repointing
an entrypoint does. See [the manifest reference's `signature` field](/reference/manifest/#signature) and
the published
[certificate](/schemas/macrodeck-certificate-v1.schema.json) and
[package signature](/schemas/macrodeck-package-signature-v1.schema.json) schemas for the exact shape.

**The trust anchor moved out of the host.** It used to sit in the host's own domain layer, referenced by
nothing but its own test; it now lives in `MacroDeck.Signing`, next to the code that verifies against
it, as `MacroDeck.Signing.MacroDeckRootKey` (the type `MacroDeckHost.Domain.Security.MacroDeckRootKey`
this section used to cite no longer exists). It carries the public half of an offline Ed25519 key pair,
verification-only; the private half never exists on a build machine or in CI, and is not any of the
release-signing keys. `--root-public` on `sign` and `verify` can point at a different root for testing,
and both commands say so loudly - a `non-production-root` warning, and a result that is not anchored to
the Macro Deck root.

**Store artifacts are signed by the Creator Portal, not by their author.** Publishing runs as Trusted
Publishing: a plugin's CI workflow authenticates to the Creator Portal with its own workload identity, the
Portal verifies that the workflow is a trusted publisher and checks the run's provenance, and the Portal
signs the artifact server-side. **Plugin developers and their CI workflows do not generate, receive,
manage or hold signing keys, certificates or signing credentials**, so there is no signing secret to store
in a repository or in a CI secret store, and no manual artifact upload in the publishing path. Certificate
issuance and revocation live in the Creator Portal too. See
[Publishing to the Store](/guides/publishing/) and
[ADR 0042](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0042-plugin-signing-and-trusted-publishing.md).

**The CLI signs and verifies packages; it never issues trust.** There is no root key generation, no
certificate issuance and no registry signing anywhere in the public CLI, and its `keygen`/`sign` pair is
not the Store path - it exists for artifacts distributed outside the Store and for Macro Deck's own
infrastructure. Where it does run: `keygen` produces a creator Ed25519 key pair and nothing else, and
`sign` requires a certificate only the Creator Portal can issue, validates the whole artifact against it,
and embeds the signature. `verify` - the one command a plugin author has any reason to run against a
signed artifact - checks the certificate chain, the certificate's validity **at the signature's own
`signedAt`** rather than at verify time, the canonical digest, and every declared file's hash and size.
See the CLI guide's [`sign`](/cli/signing/#sign) and [`verify`](/cli/signing/#verify) sections
for the full option and exit-code reference.

**`verify` reports cryptographic facts only - it never consults revocation.** The CLI states on every run,
on both output formats, that revocation was not checked: a `valid` verdict from `verify` is a fact about
the signature and certificate chain at signing time, not a live trust decision. The host does consult a
revocation source, but the one that ships answers `Unavailable` always, and an unavailable answer
deliberately does not block - failing closed on a feed that does not exist would refuse every signed
plugin on every machine. So a leaked signing key still has to be contained by treating everything signed
with it as untrusted by other means. For the Store that is a Creator Portal concern, because the keys are
the Portal's; for an artifact signed outside the Store, see
[private-key handling](/cli/signing/#private-key-handling).

**`signedAt` is not itself authenticated.** No canonical digest covers it, so a holder of the signing key
can set it to any value. Evaluating certificate validity at `signedAt` is therefore advisory against that
key holder, not an enforceable boundary - the timestamp only protects a package from being invalidated by
its own certificate's later expiry. Revocation, once it exists, is the control that actually stops a
compromised or misused key.

The host resolves a package to exactly one verdict, and everything that is not `Trusted` or a permitted
`Unsigned` refuses the install:

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

`Unsigned` is the only verdict a confirmation can admit: a local path or an upload at any time, and a
store or registry download only while the host itself reports Developer Mode on. An unknown signature
algorithm fails closed rather than being treated more leniently than no signature, and once a plugin has
been admitted as `Trusted` an unsigned update to it is refused regardless of consent.

The `publisher` block remains a claim the artifact makes about itself. It becomes an attribution only
behind a `Trusted` verdict, and verified publisher identity comes from the Creator Portal having checked
the publishing workflow's provenance before signing, never from the manifest asserting it. Run
`macrodeck-plugin verify` yourself, or in CI, to get a cryptographic verdict on an artifact before it
reaches a host.

**Only plugins carry a publisher signature.** Icon packs and profile templates installed from the Store
are authenticated by the signed registry itself - the pinned root key signs the registry's certificate,
that certificate signs the registry manifest, and the manifest carries the digest and size of every file
including the release manifest that declares each artifact's own digest. That chain proves the bytes are
the ones the Macro Deck registry published, and nothing more: those kinds are never presented as
publisher-verified, and Macro Deck does not display a verified publisher for them.

## Permissions: declared, not enforced

A manifest may declare permissions from a fixed vocabulary (`host:variables` through `device:usb`),
mirroring the host callback surface one for one. They are parsed, shape-validated, persisted and
surfaced through the installed-plugins API so a consent surface has something real to render.

**Nothing in the host gates a host callback on whether the calling plugin declared the permission that
covers it.** ADR 0029 states the position directly: this is a sequencing decision, not an oversight -
every plugin that exists today declares no permissions, and self-registering development plugins carry
no manifest at all, so "a default-deny posture would break every plugin already running, and a
default-allow posture would not be a security boundary in any sense worth calling one." Enforcing them
at the host callback router is a named follow-up that needs its own ADR.

Treat declared permissions as documentation of intent. Do not treat them as a constraint on what a
plugin can reach.

## Threat-model limitations

Read this section before deciding what a plugin should be trusted with.

- **A plugin runs with the user's full privileges.** A managed plugin is launched as an ordinary child
  process; there is no sandbox, no container, no separate user account and no privilege reduction. It
  inherits the host's user context, and can do anything that user can do - read and write their files,
  open network connections, start other processes. Installing a plugin is equivalent, in what it can
  reach, to running any other program the user downloaded.
- **Permissions are not a boundary.** See above. A plugin that declares nothing can still reach every
  host API.
- **An unsigned plugin is still admitted on your say-so.** See
  [above](#signing-the-creator-portal-signs-and-the-host-verifies-before-install-and-before-every-load).
  The host verifies signatures at install and on every launch, but nothing published today is signed, so
  a local or uploaded package you confirm yourself - or, in Developer Mode, a store package you confirm
  after it was refused - installs unverified, and for it the declared-digest
  check is corruption detection, not a boundary, because whoever can rewrite the binary can rewrite the
  unsigned manifest describing it. For those installs, trust still rests on where you got the file from.
- **Revocation is enforced but never fed.** A revoked certificate refuses the install and the launch, but
  Macro Deck publishes no revocation feed, so nothing is revoked in practice. An unavailable answer
  deliberately does not block. A compromised signing key has to be contained by other means until a feed
  ships.
- **Any local process can reach the loopback listener** and is trusted as admin on the private port.
  The model defends against LAN callers and browsers, not against a hostile process already running as
  the same user.
- **The plugin secret and the session token cross plain HTTP** on loopback. The local-only rule
  confines that exposure to processes already on the machine rather than anything reachable over the
  LAN, but it is not encryption.
- **A self-signed public certificate proves nothing about identity.** It encrypts the link; it does not
  authenticate the host to a stranger, and a DHCP change means regenerating it.
- **Isolation is not a supervision goal.** What supervision does provide is hygiene and containment of
  *failure*, not of intent: the child's environment is scrubbed of inherited `MACRO_DECK_PLUGIN_*` and
  `ASPNETCORE_URLS` variables, the listener port is bound by the host and handed to the child rather
  than reported by it, a fresh credential is minted per launch and discarded on exit, and a restart
  budget stops a crash-looping plugin. See
  [ADR 0029](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0029-plugin-packaging-installation-and-supervision.md).
- **A hard kill is a hard kill.** If a plugin does not exit within its graceful timeout, the process
  tree is killed. An SDK that does not recognise close code `4004` gets no advance notice and no chance
  to flush - ADR 0029 calls that case "kill-only".
- **macOS arm64 and unsigned binaries.** An unsigned, freshly extracted Mach-O needs at least an ad-hoc
  signature on Apple silicon before the kernel will run it, and the installer does not add one. ADR
  0051 flags this rather than solving it.

## If you find a vulnerability

Report it privately to the maintainers through
[the main repository](https://github.com/Macro-Deck-App/Macro-Deck) rather than opening a public
issue describing how to exploit it.

## See also

- [Authentication](/sdk/authentication/) - credentials, session exchange, and what a plugin must
  never do with either.
- [Plugin hosting](/sdk/hosting/) - the artifact format, the installer's rejection rules, and what
  the supervisor injects.
- [Compatibility policy](/policies/compatibility/) - what is frozen and what may change.
