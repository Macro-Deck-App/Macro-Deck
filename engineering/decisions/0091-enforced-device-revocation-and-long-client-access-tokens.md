# ADR 0091: Enforced device revocation and long client access tokens

Status: Accepted

## Context

An access token is a signed JWT and carries no server state. The host had exactly one stateful check on
one: `AccessTokenCutoff`, set by a password change or reset, which refuses every token issued at or
before that instant.

Signing a device out was not enforced that way. `DeviceService.LogoutDevice` revoked the device's refresh
tokens, pushed `DeviceSessionRevokedEvent` to the device, and put the device on an in-memory deny list for
`AccessTokenLifetime + 30s`. That list was consulted in one place, the UI WebSocket connect path. Plain
REST calls were not checked at all, so a signed-out device kept full API access - including
`POST /api/actions/run` - until its token expired. At a 15 minute lifetime the gap was small enough that
nobody had to think about it, and the deny list's window was derived from the lifetime for that reason.

That coupling is what made the lifetime hard to change. Deck devices are wall-mounted tablets and phones
on the local network, and a 15 minute token means a refresh roughly every fourteen minutes: around 35,000
rotations per device per year, each one an opportunity for the lost-response problem that
[ADR 0083](0083-companion-pairing-code-and-year-long-refresh.md) keeps having to absorb. Background tabs
and sleeping phones get throttled timers and can miss the window entirely, which costs a burst of 401s and
a reconnect on wake.

## Decision

**A device session revocation is durable and checked on every request.** `device.d_sessions_revoked_at`
records when a device was signed out. `DeviceSessionGuard` holds that per device, seeded from the device
rows before the first request is served and updated from there on. `OnTokenValidated` refuses a token
whose device claim is revoked at or before its `iat`, next to the existing cutoff check, so REST and the
WebSocket enforce the same rule. The in-memory deny list and its lifetime-derived window are gone.

**An unknown device claim is refused.** A deleted device leaves no row to seed from, so the guard has no
entry and the token is refused rather than ignored. Both paths that create a device row track it with the
guard: `DeviceService.RegisterOrReuse` for a client device and `PluginDeviceRegistry.RegisterAsync` for a
provider one. A provider device cannot hold a session at all, because `DeviceService.Resolve` refuses any
credential resolving to a row with an empty secret, but the fail-closed rule should not rest on that.

**A device signing in again inside the revocation second still works.** `DeviceSessionGuard.IssuedAtFor`
pushes a new token's `iat` past the revocation, the same way `AccessTokenCutoff.IssuedAtFor` already does
for a password reset.

**The long lifetime is earned by the device claim, not by the scope.** A client-scope session that names
a device - a deck device or the companion - gets 60 days. Admin scope keeps 15 minutes, and so does a
client-scope session without a device, because nothing can revoke a token the guard cannot see
(`AuthDefaults.AccessTokenLifetimeFor(scope, deviceId)`).

**The 60 days ship unconditionally.** `DeviceRegistration` carries the client's `AppVersion`, so the
long lifetime could be gated on a companion version that declares support. It is not: every
client-scope session that names a device gets it, including already-installed companion builds, which
see `expiresInSeconds` go from 900 to 5184000 the moment their host updates. Accepted deliberately, with
the risk - a 32-bit millisecond timer in an old build - carried to the companion repository instead, as
Macro-Deck-Companion issue 69.

**A detected reuse ends the device's access, not only its refresh tokens.** The leak that lets a
rotated token come back carries the access token with it, and that one now outlives the refresh token by
up to 60 days. `RevokeFamilyAsReuse` therefore runs the device's sign-out as well when the token names
one. A deviceless session has nothing to revoke by and keeps its 15 minute bound.

**A password change ends sessions the way a reset does.** `ChangePassword` previously revoked only the
refresh tokens, which left its access tokens alive; ADR 0003 recorded 15 minutes as the accepted bound
for that. It now sets `AccessTokenCutoff` and runs `EndAllSessions` as well.

## Consequences

- Signing a device out or deleting it takes effect on its next request instead of whenever its token
  happens to expire, and it survives a host restart. This is what makes a 60 day client token defensible;
  the two halves of this ADR must not be separated.
- A client-scope token outlives `setTimeout`'s 2^31-1 ms ceiling (about 24.8 days), which silently fires
  at once rather than late. The web client arms the refresh timer in chunks and re-arms
  (`MAX_REFRESH_DELAY_MS`). The desktop UI is pinned to admin scope in `app.config.ts` and never holds a
  token this long, so it is left as it was.
- Every credential change - reset, password, username - now records the revocation per device through
  one `EndIssuedAccessTokens`. `AccessTokenCutoff` lives only in memory, so a restart used to bring back
  every access token it had refused; at 60 days that would have been a real hole rather than a 15 minute
  one. The durable half only covers tokens that name a device: an admin or deviceless token issued
  before the change is accepted again after a restart, which stays bounded by its 15 minute life.
- The access cookie's `Expires` follows the token, so a deck device's `Path=/` cookie is now a 60 day
  persistent cookie rather than a 15 minute one. It is `HttpOnly` and `SameSite=Strict`, it is only
  `Secure` when the session was established over HTTPS, and the guard revokes it with everything else.
- Renaming the account now signs every device out at once, where it used to leave them working for up
  to 15 minutes. Same for a password change.
- A deck device refreshes roughly six times a year instead of 35,000. Rotation stays on, so the refresh
  token's own 365 days still renew on every use.
- A stolen client access token is useful for up to 60 days unless the user signs that device out, which
  now actually ends it. Before, the same theft was bounded at 15 minutes by expiry alone.
- The guard is authoritative for device claims, so any future path that creates a device row without
  tracking it would have its tokens refused until the next restart reseeds the guard.
- The rotation grace from [ADR 0083](0083-companion-pairing-code-and-year-long-refresh.md) is bounded
  either way: `RefreshTokenReuseGrace` from the rotation, or `RefreshTokenReuseGraceAfterRestart` from
  the moment this host began serving - and the second only for the rotation the previous host was
  interrupted at, judged against the last one it recorded (`ILastServedRotation`, a file written on every
  rotation). Without that bound any token rotated inside the 30 day retention would have been replayable
  for the whole window after every restart.
- What that bound cannot see is whether the previous host lived on after its last rotation. A host that
  crashes long after the last rotation it served still offers that one rotation's predecessor the window,
  if its successor was never spent, and a host that crash-loops re-opens the window on every start.
  Closing it would need a liveness signal of its own; a timer was written and discarded, because it also
  had to be silenced on a key-ring-locked host, which serves no rotation but would otherwise keep
  stamping and destroy exactly the case the window exists for.
- A device that first reaches a restarted host later than the window is still judged as reuse and loses
  its own session. That is one device rather than the account, which is what makes it tolerable.

## References

- [ADR 0003](0003-loopback-trust-token-scopes-and-device-identity.md),
  [ADR 0083](0083-companion-pairing-code-and-year-long-refresh.md)
- `host/src/MacroDeckHost.Application/Auth/DeviceSessionGuard.cs`, `AuthDefaults.cs`,
  `V1_18_0_0__AddDeviceSessionRevocation.sql`
