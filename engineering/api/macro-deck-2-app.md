# Macro Deck 2 app

The Macro Deck 2 mobile app (Macro-Deck-App/Macro-Deck-Client-App) cannot speak the Macro Deck 3 UI protocol,
but it recognises a Macro Deck 3 host and, on iOS, transfers the paid app purchase to it as a Companion
license. It has no reader in this repository and may stay installed unchanged indefinitely, so the answers it
relies on are a compatibility commitment. They are pinned by
[LockedHostGateTests](../../host/tests/MacroDeckHost.Tests.UnitTests/Api/LockedHostGateTests.cs),
[AuthPolicyMatrixTests](../../host/tests/MacroDeckHost.Tests.UnitTests/Auth/AuthPolicyMatrixTests.cs) and
[LegacyAppLicenseTransferEndpointTests](../../host/tests/MacroDeckHost.Tests.UnitTests/Api/LegacyAppLicenseTransferEndpointTests.cs).

## Detection

The app probes the configured address and `http://<host>:8193` with `GET /api/auth/status`. A `200` JSON
object with a boolean `setupComplete` identifies a Macro Deck 3 host. The endpoint stays anonymous and keeps
answering while the key ring is locked.

## Host identity

Before transferring, the app calls `POST /api/auth/identity` like the Companion does
([ADR 0086](../decisions/0086-host-identity-key.md)). A locked host answers `503 {"error":"KeyRingLocked"}`,
which the app shows as "unlock Macro Deck 3".

## Purchase transfer

`POST /api/legacy/md2-app/license-transfer`, anonymous, with
`{ "platform": "app-store-legacy", "legacyKind": "appTransaction", "signedPayload": "<AppTransaction JWS>" }`.
The controller is
[LegacyAppLicenseTransferController](../../host/src/MacroDeckHost/Api/Controllers/LegacyAppLicenseTransferController.cs).

- `200 { "status", "code" }`, `code` always present: `transferred`, `alreadyTransferred`, `pending` (the queue
  keeps retrying; `code` is the Platform's when it gave one) or `rejected` (`code` is the Platform's problem code,
  or `unsupported-source`, `invalid-proof`, `bundle-mismatch` when the host refuses the body itself, or
  `license-not-accepted` when the Platform issued a license this host does not trust).
- `503 {"error":"KeyRingLocked"}` while locked, another `503` when the proof could not be queued, `429` over the
  rate limit. The app shows any other answer as "not supported by this Macro Deck 3 version".

The proof goes through the same queue as Companion proofs ([ADR 0087](../decisions/0087-companion-license-token.md)).
The request waits for at most 10 seconds for one issuing attempt; on a host that has not used licensing yet the
revocation list is fetched first and can use part of that time, so a slow Platform answers `pending`.

## Security

The endpoint is anonymous and the app sends over plain HTTP on the LAN, like the Companion's default. What that
exposes, deliberately:

- A captured proof yields the same license for someone else; licenses are not host bound and the Platform is
  idempotent per purchase, so the buyer loses nothing.
- Any LAN caller, and any website through the open CORS policy, can learn whether this host holds a production
  license (`alreadyTransferred`).
- The limits are 5 requests a minute per address and 20 a minute for the whole host, so a LAN caller can keep
  real transfers waiting for a minute at a time. At most 2 transfers wait in the queue and they never displace a
  Companion's proof. Junk proofs that pass the unverified `bundleId` check can still reach the Platform and churn
  the temporary entries of the refused-proof cache.
