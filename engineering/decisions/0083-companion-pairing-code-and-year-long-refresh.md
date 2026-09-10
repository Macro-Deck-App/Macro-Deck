# ADR 0083: Companion pairing code and year-long refresh tokens

Status: Accepted

## Context

The connect QR code in the desktop app's network panel carried an empty `token`, so the companion app
(Android, iOS) had to ask for the account password. A QR code that stays small, and a code a person can
type on a device without a camera, need a short credential. The redeem endpoint is anonymous and the host
serves the local network, where an attacker can change address freely, so a short code needs guessing
protection that does not depend on the caller's address.

Client refresh tokens lasted 30 days. A wall-mounted phone or tablet that is switched off for longer had to
be paired again.

## Decision

**One pairing code at a time.** The host keeps a single six digit numeric code (`PairingCodeStore`), drawn
from `RandomNumberGenerator`. It is minted only through the loopback-only `GET` and `POST
/api/auth/pairing-code`, which the network panel calls: `POST` on every open replaces the code, `GET`
returns the live one and mints a new one when there is none or it has expired. A code lives 15 minutes. A
new code invalidates the previous one at once.

**Redeemed through device enrollment.** The companion redeems the code at the existing `POST
/api/auth/device-enrollment/redeem` with `{ token, device }`, and gets the same client-scope session and
cookies as an enrollment token. A token of exactly six ASCII digits is judged against the pairing code, and
anything else against the enrollment store. The code has its own slot rather than an entry in
`DeviceEnrollmentStore` because that store holds several 256-bit tokens at once (Car Thing, `#enroll=`),
and spends a hash on every guess. A six digit code needs exactly one slot and a failure count across all
callers, and neither fits that store.

**Guessing protection.**

- The code is compared in constant time. The comparison and the failure count share one lock, so a
  burst of parallel requests cannot see the code more than five times.
- After five failed redeems of a code, from any callers, the code is cleared. A failure never mints one.
  The open panel's poll mints the replacement, and a closed panel leaves no code at all.
- On top of that, six digit redeems share a global `LoginThrottle` key. It uses the throttle's
  defaults: five free failures, then a lockout starting at 30 seconds, doubling to 15 minutes, and
  forgotten after an hour without failures. The per-address throttle stays.
- The result is at most five comparisons per minted code, and at most five per lockout window, against a
  one in a million code, and only while a code exists.

**Refresh tokens live 365 days**, for every scope. Rotation on every refresh and the reuse detection that
revokes every session of the account stay. The refresh cookie's expiry follows the token. Rotated and
logged-out rows are deleted 30 days after they were revoked (`AuthDefaults.RevokedRefreshTokenRetention`)
instead of at their original expiry.

## Consequences

- Reuse detection sees a rotated token for 30 days, the window it effectively had under the 30 day
  lifetime. A rotated token that returns later is refused as invalid, and does not revoke every session.
- An attacker on the LAN who keeps sending wrong codes can hold pairing by code at `429` for as long as
  they keep doing it. Password login is unaffected and is the fallback.
- A stolen refresh token is useful for up to a year of inactivity instead of 30 days. Signing the device
  out in the devices list, or changing the password, still revokes it at once.
- The code is held in memory only. A host restart invalidates it, and the panel mints a new one when it
  is next opened.
- A device kept alive by a live refresh token keeps its row, and is only purged as stale once that token
  is gone.

## References

- [ADR 0003](0003-loopback-trust-token-scopes-and-device-identity.md)
- `host/src/MacroDeckHost.Application/Auth/PairingCodeStore.cs`, `AuthDefaults.cs`
