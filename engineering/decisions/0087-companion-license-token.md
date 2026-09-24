# ADR 0087: The Companion license is a signed bearer token the host keeps and hands out

Status: Accepted

## Context

The Companion app is paid: bought once through Google Play or the App Store, or carried over from the
previous iOS app. The host needs to hold a license so that other Companions connecting to it can use it,
and it needs the trial start of each phone so that reinstalling the app does not restart the trial. The
Platform API turns a verified store purchase into a license (Macro-Deck-Platform
docs/companion-licensing.md). The Companion already checks ECDSA P-256 signatures on Android and iOS.
Ed25519 is not available on Android below API 33, and Kotlin/Native on iOS cannot use it either.

## Decision

**Token.** A license is a compact JWS, `alg` ES256, with a `kid` header. Its claims are `iss`
`https://platform.macro-deck.app`, `aud` `macrodeck-companion`, `sub` (the license id), `product`
`companion_app_license`, `source` (`google-play`, `app-store`, `app-store-legacy`, or `test` on licenses an
earlier host version issued itself) and `iat` in seconds. There is no `exp`, because the license does not
expire. The optional display claims `purchased_at`
(epoch seconds) and `billing_id` (store order or original transaction id) are shown when present; a malformed
value hides the detail and never rejects the license. The host checks it with
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

**Platform client and developer mode.** `PlatformLicenseClient` posts a purchase proof to
`api.macro-deck.app` (`POST api/v1/companion-licenses`). `google-play` and `app-store` proofs are sent, and
`app-store-legacy` proofs of kind `appTransaction` (with `legacyKind`), which only the Macro Deck 2 app submits. The base URL can be redirected with `MACRO_DECK_PLATFORM_URL` in
Development-channel builds only, for example to Platform mock mode or staging, which sign with `test-2026`.
The host issues no license itself; there is no test license button. A `test-2026` license only comes from
Platform mock mode or staging, and only works in developer mode (the `developer.mode` preference):

- the host trusts `test-2026` only while developer mode is on, so a stored test license stops being handed
  out once it is switched off;
- the status page shows the source and key id, and marks a test license.

On the Companion side only debug builds trust `test-2026`, so the test private key, which is public, cannot
unlock a release build. A debug Companion with a sandbox purchase is refused by the production Platform
(`sandbox-purchase`); a developer points a development build at Platform mock mode or staging. A test license
left on a developer machine from an earlier version is ignored once developer mode is off and replaced by any
production license; nothing removes it otherwise.

**Sync.** The `SyncCompanionLicense` request, client scope with a device claim, is answered at once with the
stored token and the known trial start; it never waits for the Platform. While no production license is stored,
a purchase proof is queued and exchanged in the background by `CompanionLicenseBackgroundService`; the issued
token is stored and pushed as `CompanionLicenseEvent` to every connected Companion, including the one that sent
the proof. One lock guards every read-modify-write of the token, the trial map and the pending proofs.

**Macro Deck 2 app purchases.** Buyers of the paid Macro Deck 2 iOS app transfer it with the anonymous
`POST api/legacy/md2-app/license-transfer` ([macro-deck-2-app.md](../api/macro-deck-2-app.md)). Its proof,
keyed by the AppTransaction's `appTransactionId`, joins the same queue; that request alone waits up to 10
seconds for one issuing attempt so the app can report the outcome. At most 2 such proofs wait at once, and
they never displace a Companion's proof.

**Issuing retries.** Pending proofs are kept in the preference `license.pendingProofs`, at most 8, each
encrypted with ASP.NET Core data protection and keyed by a hash of its purchase (the Google purchase token or
the App Store original transaction id, as the Platform keys it). A network failure, timeout, 5xx, 429 or a 403
without the `license-revoked` code is retried with exponential backoff from about 5 seconds to at most 30
minutes, with jitter and `Retry-After` honoured, and survives a restart. A purchase the store has not
completed yet (`purchase-not-found`, `purchase-not-completed`) is retried the same way for at most 7 days. Any
other refusal drops the proof and records a hash in `license.refusedProofKeys` (at most 64), so reconnecting
Companions do not send it again. A refusal about the purchase itself (refunded, revoked, cancelled, a revoked
license) blocks the purchase for good. Any other refusal, an issued license the host does not accept, or a
purchase still pending after 7 days blocks only that exact proof, and only for a day, because a Platform
misconfiguration such as a missing Apple root or a wrong package name must not block the purchase for good.
The License page shows a pending proof and its next attempt, and is told of every change with
`CompanionLicenseChangedEvent`.

**Trials.** Trial starts are kept in the preference `license.trials`, a map from trial device id to host UTC
epoch milliseconds. The host records its own clock the first time a device reports `trialStarted` and never
accepts a time from the client, so a client cannot move a start into the past. The map holds at most 1000
entries, and the oldest start is dropped first. That cap limits growth; it does not protect against abuse,
because a new pairing or a new trial id costs an attacker nothing.

**Platform revocation.** While licensing is in use (a stored license, a pending proof, a cached list or a
Companion that synced since start), the host fetches `GET api/v1/companion-licenses/revocations` hourly, and every
5 minutes up to hourly after a failure. The list (at most 10,000 ids; a longer one keeps the previous copy) is
cached in `license.platformRevokedIds`, so an offline host still applies the last one. A stored license on the
list is removed, whatever key signed it, `CompanionLicenseRevokedEvent` is pushed, and a listed id is never
adopted. The sync answer's `revokedLicenseIds` carries the Platform ids while the Platform list has at most
4,000 ids; above that it carries only the listed ids of the license the Companion submitted and of the host's
stored license, because a UI WebSocket message is capped at 256 KB. The Companion
fetches the full list from the Platform itself.

**Account sync.** While the host is signed in to a Macro Deck account (ADR 0054),
`CompanionLicenseAccountSyncBackgroundService` keeps the stored license and the license on that account in
step through `api/v1/companion-licenses/account` (Macro-Deck-Platform docs/companion-licensing.md). It reads
the account license, reconciles, then long-polls for a change and reconciles every answer:

- both valid: nothing changes, even when they differ, except that a production account license replaces a
  local test license;
- only the local license valid: it is uploaded, but only if the host may upload it (below);
- only the account license valid: it is stored, replacing an invalid or missing local one, and pushed as
  `CompanionLicenseEvent` to every connected Companion.

Valid means verified with the current trust set and not on the cached Platform revocation list. The
Platform keeps one license per account and several accounts may hold the same license.

A host uploads only a license it obtained itself: issued from a purchase proof it forwarded (a Companion
purchase or the Macro Deck 2 transfer) or taken from the account. The id is kept in
`license.accountEligibleId`. A license merely taken over from a Companion's token is never uploaded, so
connecting a phone to someone else's signed-in host never puts that license on their account. It also never
replaces a missing or invalid account license. The first locked access after an upgrade marks a license that
was already stored as uploadable, and writes `none` otherwise, before anything can be adopted; a restored
backup from before this version gets the same treatment on its next start.

A local change (a new license, a revocation drop) or a change of account status abandons the poll and starts
over. An upload the Platform answered without storing (conflict, refusal, unchanged) is not repeated while
the account, the local license, the account revision and the account license stay the same. An upload refused
as `license-revoked` drops the local license at once. Unexpected answers back off from 5 seconds to 5
minutes; an upload refused because the account is suspended or unknown stops uploads for 30 minutes while
the long poll goes on, and a session Connect itself reports as suspended stops the sync like a sign-out. A license newly
stored from a Companion, a purchase proof or the account raises an informational user notification, one at a
time (dedupe key `companionLicense.received`). The License page says when the license is saved to the
account, or asks a signed-out owner to sign in.

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
  reports it refunded or revoked. A token, once issued, can only be revoked by the platform. Refunds after
  issuing are not detected automatically; an administrator revokes the license, and the revocation list is
  how that reaches hosts and phones.
- The 1000 entry trial cap is a known ceiling. A central trial record belongs to the Platform API.
- A host that uses Companions calls `api.macro-deck.app` about once an hour for the revocation list. A host
  signed in to a Macro Deck account also keeps one long poll open there, renewed about every 50 seconds.
- Linking a license to an account is permanent: there is no unlink. The license, including its display claims
  such as the billing id, becomes readable to every host signed in to that account.
- Debug Companions no longer receive the ids of test licenses an earlier host version revoked, and a host in
  developer mode no longer refuses them.

## References

- [`CompanionLicenseTokens`](../../host/src/MacroDeckHost.Infrastructure/Licensing/CompanionLicenseTokens.cs)
- [`CompanionLicenseService`](../../host/src/MacroDeckHost/Licensing/CompanionLicenseService.cs)
- [`PlatformLicenseClient`](../../host/src/MacroDeckHost.Infrastructure/Licensing/PlatformLicenseClient.cs)
- [`PlatformLicenseAccountClient`](../../host/src/MacroDeckHost.Infrastructure/Licensing/PlatformLicenseAccountClient.cs)
- [`CompanionLicenseAccountSyncBackgroundService`](../../host/src/MacroDeckHost/Licensing/CompanionLicenseAccountSyncBackgroundService.cs)
- [`CompanionLicenseBackgroundService`](../../host/src/MacroDeckHost/Licensing/CompanionLicenseBackgroundService.cs)
