# ADR 0003: Loopback trust, token scopes, and device identity

Status: Accepted

## Context

Earlier iterations left every host endpoint anonymous, so anyone on the LAN could execute actions,
read secrets and browse the filesystem. Authentication had to cover clients with different trust
levels — a local desktop UI that never logs in, a read-only web client, and remote full-UI clients —
while supporting a first-run setup wizard, login with "stay signed in", and a guarantee that
web-client tokens can never reach configuration routes.

Clients also needed a stable identity across reloads. The Angular clients generated a UUID in memory,
so every page reload produced a new one: the host could not rename a device, sign a specific browser
out, or target a later action at a known device, and the `Client Connected` / `Client Disconnected`
events fired on every reload. Making the id merely persistent in `localStorage` fixes the reload and
nothing else — any client can read another device's id off the Devices tab.

## Decision

### Two listeners, one trusted

The host binds a public listener reachable from the LAN (see
[ADR 0040](0040-public-listeners-and-tls.md)) and a private loopback listener whose port the
bootstrapper assigns. `LoopbackConnection.IsTrusted` treats a request as the desktop UI only when it
arrives **on the loopback listener's port**, from a loopback remote address, with a loopback `Host`
header. The `Host` check is a DNS-rebinding guard.

The port clause is a **positive** match, and that is load-bearing. It once read "local port is not the
public port", which trusted every port but one and would have implicitly trusted any further listener
the host learned to bind — and ADR 0040 added exactly such a listener. While the loopback port is not
yet published the predicate denies trust, so the window fails closed: a 401 the desktop UI retries,
never an unauthenticated admin.

`IsLocalRequest`, the weaker check gating the plugin endpoints, has no port clause by design and
therefore also matches a device reaching loopback through an `adb reverse` tunnel
([ADR 0030](0030-android-usb-connections-over-adb.md)). Those endpoints stay credential-gated and
their throttles are keyed on credentials rather than on the source address, so reachability widens but
nothing is granted.

### Capability scopes, deny by default

There is one user, but tokens carry a `scope` claim of `admin` or `client`. `SetFallbackPolicy`
requires admin, so an endpoint without explicit authorization metadata is admin-only; viewer-safe
endpoints opt into the `ClientAccess` policy. A missing annotation therefore fails closed.

`POST /api/auth/login` issues a 15-minute access token plus a rotating refresh token whose SHA-256
hash is stored; reuse of a rotated token is treated as compromise and revokes all sessions. The
refresh token is an HttpOnly cookie scoped to `/api/auth`. The access token is also set as a
`Path=/` cookie so `<img>` and font URLs authenticate without a header — it authenticates GET and
HEAD only, never mutations, which keeps it CSRF-safe. CORS is configured without `AllowCredentials`,
so those cookies are never sent cross-origin.

`POST /api/auth/setup` is loopback-only, so a LAN device can never claim the single account first.

### Device identity is a login-time credential, not a second factor

A device is a persisted row with an immutable id, a server-owned friendly name, self-reported
metadata and a hashed secret. Refresh tokens carry the device id; access tokens carry a `device`
claim, read only off the validated principal so a client cannot assert a device it holds no token for.

- **Registration rides on login.** There is no anonymous device endpoint, so an unauthenticated LAN
  caller cannot create rows.
- **The secret is an anti-spoofing measure.** It is only ever presented with the account password and
  grants nothing by itself; its single job is to stop somebody who already knows the password from
  claiming a device id they read off a screen.
- **Missing, unknown and wrong credentials are indistinguishable.** All three silently mint a new
  device, so login is not an oracle for which device ids exist.
- **The secret is issued once and never rotated.** Rotation fails badly on a flaky network: a lost
  response leaves the host rotated and the client not, and the next login mints a duplicate row.
  Duplicate devices are worse than a long-lived secret that is worthless without the password.
- **Logout, not blocking.** Signing a device out revokes its refresh tokens and aborts its live
  connections; there is no persistent blocked flag, because the failure mode of blocking the wrong
  device is losing access to the UI that unblocks it. Removal additionally deletes the row and gates
  reconnects for the access-token lifetime.
- **The loopback desktop UI is not a device.** It never logs in, so every device-less path is a
  supported state.

## Consequences

- The desktop UI stays credential-less and loads over the private loopback transport without a login.
- The web client cannot rewrite widget actions: client scope reaches only whitelisted runtime routes.
- Signing a device out does not invalidate its already-issued access token, so it retains REST and
  media access for up to 15 minutes. This matches the existing behaviour of a password change.
- Clearing browser storage registers a new logical device; stale rows without a live session are
  purged after 60 days. Copying browser storage to a second browser makes both act as the same device
  — accepted, since the credential is not an independent factor.
- An XSS in either SPA can read the device secret. It grants nothing without the password.
- A device is not a tab: ordinary press-triggered navigation resolves against `clientId` and stays in
  the tab that pressed it. Application-focus folder rules deliberately target a *device* and move
  every one of its connections.

## Alternatives considered

- **Cookie-session auth for everything.** CSRF surface, and it does not fit the credential-less
  desktop path.
- **A bootstrap token file beside the port file.** More moving parts than transport trust, with the
  same local-attacker exposure — any local process can already reach the loopback port.
- **Full ASP.NET Core Identity.** Drags a multi-user framework into a single-user system.
- **Deriving device identity from the refresh token alone.** Identity would die with every logout, so
  a renamed device could not survive signing out and back in.

## References

- [The authentication guide](../api/authentication.md) for the concrete request flow.
