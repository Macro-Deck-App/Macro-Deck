# ADR 0054: The Macro Deck Connect session is a host-owned refresh credential

Status: Accepted

## Context

Macro Deck Connect is the first cloud identity the application signs in to. Identity registers Macro
Deck 3 as a public OIDC client with no secret, requires PKCE, and issues ten-minute access tokens
alongside a refresh token that rotates on every use and expires 180 days after it was last used.
Reusing a redeemed refresh token revokes the whole authorization.

The governing requirement is not the login. It is that a user who has not opened Macro Deck for
several weeks, or who starts it while Identity is unreachable, is not signed out. Refresh failures
are therefore not equivalent: some mean the credential is gone, and some mean the network is.
Treating them alike either signs users out over a transient outage or retries a dead session forever.

Sign-in originally ran Authorization Code + PKCE against a throwaway loopback listener. The client's
redirect URIs are registered without a port, but the host must be the literal `127.0.0.1`, which ties
the flow to the machine running the host: the desktop UI opened over the public listener from another
device would point the callback at *that* device, and the authorization code would never arrive. No
custom URI scheme is registered either, deliberately - desktop scheme ownership can be claimed by
another local application.

The existing integration OAuth path does not fit. It derives its redirect from the *public* listener,
which the user can disable, at a path Identity would reject, and its callback registrations neither
expire nor are single-use. Its `IOAuthSession` is also a public SDK contract with no PKCE members.

## Decision

The session is owned by the host, exposed through one `IConnectSessionService`, and surfaced to
clients as a snapshot. Future cloud features consume that service rather than adding a second login.

Sign-in runs the **device code grant** (RFC 8628). It carries no redirect URI at all, which is what
removes the `127.0.0.1` constraint and lets the desktop UI start a sign-in from wherever it is being
used. The host requests a device authorization, hands the client the issuer's `verification_uri`, its
`verification_uri_complete` and the user code, and polls the token endpoint until the authorization
resolves. Because the complete URI already carries the code, the normal path is one confirmation in a
browser and nothing to type.

The poll is closed over the four answers RFC 8628 §3.5 defines: `authorization_pending` waits,
`slow_down` lengthens the interval, `access_denied` ends the attempt as declined, and `expired_token`
ends it as expired and restartable. `access_denied` means something different here than on the
refresh path, where it means the account is suspended, so the device poll classifies its own errors
rather than sharing that mapping. Everything after the first token pair - rotation, refresh, backoff,
offline handling, suspension, storage and the state machine - is unchanged and shared with the
credential the loopback flow used to produce.

Session state is two orthogonal axes. The status — signed out, signing in, signed in,
reauthentication required, suspended — is the durable identity. Connectivity — ok or offline — is
reachability and is only meaningful while signed in. Refreshing is not a state.

Failure classification is closed and biased toward keeping the session:

- `invalid_grant` is terminal. The credential is deleted and interactive sign-in is required. This
  covers a revoked, reused or expired token, a rotated security stamp from a password or role change
  or an admin sign-out, and a deleted or locked-out account.
- `access_denied` means the account is suspended. Refreshing **stops** and the credential is
  **kept**, because suspension does not revoke the authorization and must recover when lifted.
- Everything else is transient: the session stays signed in, connectivity flips to offline, and the
  credential is never touched.

Rotation is persisted through a single serialized queue. A write that exceeds its budget is left
queued rather than abandoned; abandoning a rotation mid-write is how a session is permanently lost.

The refresh token is stored through `ISecretService`, which protects it with ASP.NET Data Protection.
`IConnectCredentialStore` is a narrow seam so an OS-keychain implementation can replace it without
touching the flow.

The whole account surface - reading the session and the avatar as much as signing in, cancelling and
signing out - requires the `admin` scope. A loopback connection authenticates as admin, so the
desktop app in its own window and the desktop UI opened over the public listener are both covered by
that one policy, and neither needs a loopback-only exception any more. The web client, which holds a
`client`-scope token, reaches none of it: it is deliberately unaware that Macro Deck accounts exist,
rather than being shown an account it cannot act on. The state-change notification broadcast to
clients carries no fields at all; clients re-read the session rather than trusting a payload.

Identity claims are read from the ID token without validating its signature against JWKS. The token
arrives over a direct TLS channel from the issuer's token endpoint, and it is used only to render a
name and an avatar.

Backups never carry the credential. It is removed from the database snapshot before the archive is
written, and `connect.` preference keys are refused on restore.

## Consequences

- Storage is only as strong as the Data Protection key ring. `IConnectCredentialStore` is a seam for
  a per-credential keystore, but the key ring is the shared root of every secret in the installation,
  so protecting *it* is the better fix. That is what
  [ADR 0047](0047-secrets-backups-and-restore.md) does: the ring is wrapped by a key held in
  the platform keystore, and stays readable on disk only where no keystore is available or the
  installation is portable.
- A session survives arbitrary offline time but not more than 180 days without launching, because the
  sliding window is only re-based by a successful refresh. An unconditional guarantee would require a
  server-side change.
- A suspended account needs a retry floor that survives restarts; without one, relaunching the app
  repeatedly becomes an unbounded retry loop by another route.
- Copying a data directory to a second installation gives both the same refresh token. They rotate it
  independently, and the second use is a reuse that revokes the authorization for both.
- A restore comes back signed out, including a same-machine restore.
- The moment a cloud feature makes an authorization decision from these claims rather than displaying
  them, JWKS validation stops being optional.
- Sign-out revokes this installation's authorization only. Other installations keep their own.
- An admin token is now enough to bind this installation to an account, from anywhere on the network.
  That is the point of the change, and it makes the admin credential the whole boundary; there is no
  second, physical factor behind it any more.
- The user has to confirm on a page that is not the one they started from, and a sign-in can now fail
  in ways the loopback flow could not: declined, or expired before anyone confirmed. Both end in a
  state the UI can restart, and are carried to clients as a closed set of reasons rather than as
  server-rendered text, so each client renders them in its own language.
- Nothing binds the confirmation to the device that asked for it. Whoever holds the code can approve
  it with their own account, so a code shown on a shared screen signs the installation into whichever
  account confirms it.

## References

- [Issue #524](https://github.com/Macro-Deck-App/Macro-Deck/issues/524)
- [Issue #673](https://github.com/Macro-Deck-App/Macro-Deck/issues/673) - the device code grant
  replacing the loopback callback
- [RFC 8628 - OAuth 2.0 Device Authorization Grant](https://www.rfc-editor.org/rfc/rfc8628)
- [ADR 0003 - Loopback trust, token scopes, and device identity](0003-loopback-trust-token-scopes-and-device-identity.md)
- [RFC 8252 - OAuth 2.0 for Native Apps](https://www.rfc-editor.org/rfc/rfc8252)
