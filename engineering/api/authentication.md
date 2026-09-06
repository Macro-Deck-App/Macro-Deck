# Authentication and Authorization

Macro Deck 3 separates the trusted desktop transport from untrusted network clients. This document records the security model and invariants; source code remains authoritative for exact token lifetimes, cookie names, endpoint lists, and implementation details.

## Trust model

The desktop UI reaches the host through a dedicated loopback listener and is implicitly authenticated as admin only when the loopback trust predicate succeeds.

Public/LAN clients use normal JWT authentication. Authorization has two coarse scopes:

- `admin` for full configuration access.
- `client` for the public deck/client surface.

Authorization is deny-by-default. Endpoints without explicit wider authorization remain admin-only.

The relevant implementation is under `host/src/MacroDeckHost/Auth/` and `host/src/MacroDeckHost/Api/Controllers/AuthController.cs`.

## Loopback trust

Loopback trust is a transport property, not simply `remote IP == 127.0.0.1`.

A request is trusted only when it arrives through the private listener and satisfies the host's loopback/Host-header checks. The Host-header restriction is required to prevent DNS-rebinding attacks from turning an arbitrary local browser request into an admin request.

Public listeners never inherit loopback trust, even when reached through a local address. Transport mechanisms such as ADB reverse tunnels therefore terminate on the public listener and authenticate like any other client.

See [ADR 0003](../decisions/0003-loopback-trust-token-scopes-and-device-identity.md) and [ADR 0030](../decisions/0030-android-usb-connections-over-adb.md).

Backup download, import, restore, recovery-key reveal, and recovery-key regeneration are loopback-only: each one reads or writes the reusable plaintext secret described below, so a network client must never reach it. Backup list, create, delete, settings, and recovery-key *state* (whether a recovery key exists, not its value) stay available on the public listener, since they do not expose that content. See [ADR 0047](../decisions/0047-secrets-backups-and-restore.md).

## JWT clients

Untrusted clients authenticate with short-lived access tokens and refresh credentials managed by the host. Access tokens carry the caller's scope and, when applicable, device identity.

Bearer tokens are the normal API authentication path. The UI WebSocket never accepts a bearer token in its URL: clients first mint a short-lived one-time ticket through the authenticated HTTP endpoint. Safe media requests can use the host-supported cookie path where headers cannot be supplied by browser elements.

Token validation, extraction rules, lifetimes, signing-key persistence, and cookie settings are implemented in the authentication source. Do not duplicate their current literal values here.

## Device identity

A logged-in network client can have persistent device identity so individual clients can be named, listed, signed out, or permanently removed. The trusted desktop UI does not log in and therefore is not represented as a normal authenticated device. Permanent removal revokes every refresh token for that device, pushes session revocation, aborts its live connections, and deletes the registration; an already-issued access token may remain usable until its normal expiry. The removed device id and secret cannot restore the old registration: a later login with them mints a fresh device identity.

Device identity is separate from authorization scope: it identifies a client; it does not grant additional permission.

See [ADR 0003](../decisions/0003-loopback-trust-token-scopes-and-device-identity.md).

## HTTPS and browser security features

The public listener can be configured for HTTPS. Browser features that require a secure context, such as service workers and wake locks, require a certificate the client device actually trusts and an origin covered by the certificate.

Changing between HTTP and HTTPS changes the browser origin, so browser-local state and credentials do not automatically move between them.

See [ADR 0040](../decisions/0040-public-listeners-and-tls.md).

## Security rules for changes

When adding or changing an endpoint:

- Keep the fallback authorization policy restrictive.
- Widen access explicitly only when the client scope requires it.
- Do not use loopback address alone as proof of trusted-desktop intent.
- Keep state-changing cookie authentication paths protected from CSRF by the existing method/scope rules.
- Never return stored credential hashes or reusable plaintext secrets. A full backup archive counts as one: it contains the Data Protection key ring, the auth token signing key, the TLS private key, and every `secret` row.
- Treat access tokens, refresh tokens, plugin credentials, OAuth codes, and authorization headers as secrets in logs.
- Add a security-focused regression test when changing trust selection, scope evaluation, token handling, or credential revocation.

Plugin-process authentication is a separate public protocol contract. See the [plugin protocol documentation](https://docs.macro-deck.app/reference/protocol/) and [ADR 0028](../decisions/0028-plugin-credentials-and-pairing.md).

## Macro Deck Connect

Signing in to Macro Deck Connect is separate from the authentication described above. That is the
host's own account, protecting this installation; Connect is a cloud identity the installation signs
in to. Neither grants the other anything, and no Connect claim is used to authorize a host request.

Sign-in uses the device code grant. The host requests a device authorization, hands the client the
issuer's verification URI and user code, and polls until the authorization resolves; because the
complete URI already carries the code, the normal path is one confirmation in a browser and nothing
to type. The whole account surface requires the `admin` scope, so the desktop app in its own window
and the desktop UI opened over the public listener are both covered by one policy, while the web
client — which holds a `client`-scope token — is deliberately unaware that Macro Deck accounts exist.
The change notification broadcast over the UI transport carries no fields, so clients re-read the
session rather than trusting a payload.

Nothing binds the confirmation to the device that asked for it: whoever holds the code can approve it
with their own account, so a code shown on a shared screen signs the installation into whichever
account confirms it.

The refresh token is stored through `ISecretService` and is therefore protected by ASP.NET Data
Protection, never written as plaintext into configuration or data files. The key ring behind that is
itself wrapped by a key held in the platform keystore - the Windows Credential Manager, the macOS
keychain, or the Secret Service over libsecret - and escrowed under the backup recovery key, so a
copy of the data directory no longer carries the material that opens it. Where no keystore is
available, and in a portable installation, the ring stays readable on disk and the host reports that
state rather than implying protection it does not have. This is not specific to the Connect
credential: the same key ring protects every secret in the installation, which is why the ring itself
was protected rather than one credential moved into a keystore. `IConnectCredentialStore` remains a
seam if a per-credential store is ever wanted. See
[ADR 0047](../decisions/0047-secrets-backups-and-restore.md).

Backups never carry the credential: it is removed from the database snapshot before the archive is
written, and `connect.` preference keys are refused on restore, so a restored installation — on this
machine or another — comes back signed out. See [ADR 0054](../decisions/0054-connect-session-is-a-host-owned-refresh-credential.md).
