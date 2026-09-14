# ADR 0087: The Companion license is a signed bearer token the host keeps and hands out

Status: Accepted

## Context

The Companion app is paid: bought once through Google Play or the App Store, or carried over from the
previous iOS app. The host needs to hold a license so that other Companions connecting to it can use it,
and it needs the trial start of each phone so that reinstalling the app does not restart the trial. The
Platform API that turns a store purchase into a license does not exist yet. The Companion already checks
ECDSA P-256 signatures on Android and iOS. Ed25519 is not available on Android below API 33, and Kotlin/Native
on iOS cannot use it either.

## Decision

**Token.** A license is a compact JWS, `alg` ES256, with a `kid` header. Its claims are `iss`
`https://platform.macro-deck.app`, `aud` `macrodeck-companion`, `sub` (the license id), `product`
`companion_app_license`, `source` (`google-play`, `app-store`, `app-store-legacy` or `test`) and `iat` in
seconds. There is no `exp`, because the license does not expire. The host checks it with
Microsoft.IdentityModel.JsonWebTokens in `CompanionLicenseTokens`.

**Trust and rotation.** The trusted keys are a hardcoded map from `kid` to a P-256 public key, the same on
the host and the Companion. Rotating means adding a new `kid`. Old entries stay, so licenses already issued
keep verifying. Retiring a key means removing its entry, and every license signed with it stops verifying. An
unknown `kid` fails closed, and so does a token signed with one key but labelled with another `kid`. `prod-2026`
is always trusted. The host checks the stored token against the trust set in force on every read: the sync
answer, the push and the status page.

**Bearer.** A license is not bound to any host or device. Any valid token a Companion submits is stored if
the host holds none yet, and handed to every Companion that syncs with that host. One licensed phone therefore
licenses the hosts it connects to, and every device on those hosts. That is intended: the product sells one
license per user, not per device.

**Fake platform and developer mode.** Until the Platform API exists, `FakePlatformLicenseClient` stands in
for it. It signs with the committed `test-2026` key. The key and everything built on it only work in
developer mode (the `developer.mode` preference):

- the fake issues only while developer mode is on;
- the host trusts `test-2026` only while developer mode is on, so a stored test license stops being handed
  out once it is switched off;
- `POST api/settings/license/test` answers 404 unless developer mode is on;
- the status page shows the source and key id, and marks a test license.

On the Companion side only debug builds trust `test-2026`, so the committed private key cannot unlock a
release build.

**Sync.** The `SyncCompanionLicense` request, client scope with a device claim, is answered at once with the
stored token and the known trial start. A purchase proof is exchanged with the platform in the background,
and the issued token is stored and pushed as `CompanionLicenseEvent` to every connected Companion, including
the one that sent the proof. One lock guards every read-modify-write of the token and of the trial map.

**Trials.** Trial starts are kept in the preference `license.trials`, a map from trial device id to host UTC
epoch milliseconds. The host records its own clock the first time a device reports `trialStarted` and never
accepts a time from the client, so a client cannot move a start into the past. The map holds at most 1000
entries, and the oldest start is dropped first. That cap limits growth; it does not protect against abuse,
because a new pairing or a new trial id costs an attacker nothing.

**Revoking a test license.** `DELETE api/settings/license/test` removes a stored test license, in or out of
developer mode, and records its id in the preference `license.revokedTestIds`, a list of at most 100 ids with
the oldest dropped first. The host pushes `CompanionLicenseRevokedEvent` to every connected Companion, lists
the revoked ids in every sync answer as `revokedLicenseIds` so an offline Companion learns of it later, and
never adopts a revoked id again, so a Companion still holding the token cannot put it back. Issuing a new test
license still works, since it carries a new id. Only test licenses can be revoked this way; a stored
production license is never touched, and revoking one belongs to the platform. The License tab offers the
revocation whenever a test license is stored, whatever the developer mode, because debug Companions keep
trusting it. A debug build backed by a store purchase reports its proof again and, while developer mode is
on, the fake platform answers with a fresh test license under a new id: revocation drops a license, not a
purchase.

## Consequences

- Trial anchoring has known gaps. A new trial is possible:
  - after a factory reset;
  - on a secondary Android user or work profile;
  - on a build signed with a different key, since ANDROID_ID is per device and user on API 23 to 25 and also
    per signing key from API 26;
  - on Android devices without an ANDROID_ID, which fall back to a random id that a reinstall wipes;
  - with a host that never saw the device or that answers `unknown_type`;
  - when the iOS Keychain loses the id after the app is deleted, since that survival is undocumented and
    only best effort.
- An old host without `/api/system/time` cannot start a local trial. A connection that never had a
  successful time sync checks an existing trial against the phone clock, so a wrong phone clock shifts
  expiry for as long as the time request fails. A trial start that can never be read from the phone's
  secure store means that phone gets no local trial; a host start or a purchase still works.
- Privacy: the trial device id leaves the phone only as `sha256("macrodeck-trial:" + id)`, base64url, and
  only to the user's own hosts. The salt keeps it from being matched with ids other apps derive. The host
  stores it in its preferences, so it is part of host backups. The store privacy labels are the
  maintainer's decision.
- Refunds and revocation: a store entitlement that has no token yet is dropped on the phone when the store
  reports it refunded or revoked. A token, once issued, can only be revoked by the platform. Until the
  Platform API exists, a refunded purchase that already produced a token stays licensed.
- The 1000 entry trial cap is a known ceiling. A central trial record belongs to the Platform API.
- Replacing the fake with a real Platform API client changes only the `IPlatformLicenseClient`
  registration. The token format and the trust map stay as they are.

## References

- [`CompanionLicenseTokens`](../../host/src/MacroDeckHost.Infrastructure/Licensing/CompanionLicenseTokens.cs)
- [`CompanionLicenseService`](../../host/src/MacroDeckHost/Licensing/CompanionLicenseService.cs)
- [`FakePlatformLicenseClient`](../../host/src/MacroDeckHost.Infrastructure/Licensing/FakePlatformLicenseClient.cs)
