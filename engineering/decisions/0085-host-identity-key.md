# ADR 0085: The host proves its identity to the companion app

Status: Accepted

## Context

The companion app (Android, iOS) keeps several addresses per saved host and learns new ones from mDNS. The
host serves plain HTTP on the LAN by design, and the app matched hosts by instance name or a shared IP, so any
device on the network advertising the host's name could receive the bearer token, the refresh cookie and
the stored password. A replayed refresh token also revokes every session of the account.

## Decision

**A per-installation identity key.** The host holds one ECDSA P-256 key in `keys/host-identity.key`,
PKCS#8 wrapped by Data Protection (`MacroDeck.Auth.HostIdentityKey`). Its public identity is the 65-byte
uncompressed point, base64. P-256 needs no new dependency on .NET, Android API 23 or iOS 15; Ed25519 would.

**Challenge.** `POST /api/auth/identity` is anonymous, rate limited per remote address (30 per 10 seconds;
IPv4-mapped addresses count as IPv4, link-local ones without their zone) and, as a ceiling against a caller
rotating addresses, per IPv6 /64 (300 per 10 seconds); either limit answers `429`, and not on the locked-host allow-list. The request is `{ "nonce": "<base64, 16 to 64 bytes>" }`
in canonical standard base64 with padding, anything else is `400`. The answer is
`{ "publicKey", "endpoint", "signature" }`: a DER signature over the UTF-8 message
`macrodeck-host-identity/v1\n<publicKey>\n<endpoint>\n<nonce>`. The endpoint is the canonical authority of
the host's own socket: IPv4 dotted, IPv4-mapped addresses as IPv4, IPv6 as .NET's `IPAddress` text (lowercase,
compressed, the deprecated IPv4-compatible form printed as `::a.b.c.d`) without a zone id and in brackets, then
the port, never anything from the request, so a relay obtains a proof bound to the real
host's address and not its own.

**Trust on first use.** `TokenResponse` from login, redeem and refresh carries `hostKey`, which the app pins
after checking it against a challenge made just before. A host that cannot load its key omits the field and
signs people in as before, and the challenge answers `503`.

**Fingerprint.** A device without a camera recovers a changed identity by typing an address, and the user
then compares a fingerprint: SHA-256 over the raw 65-byte point, the first 12 bytes as uppercase hex in six
groups of four separated by single spaces (`3208 E004 6ED3 EE6B 4E75 1027` for the shared vector). The
desktop app shows it in the connection panel next to the QR code and in the network settings, from the
`identityFingerprint` field of the admin-only `GET /api/system/connection-info`, which is null while the key
cannot be loaded. The anonymous challenge does not carry it. Its strength depends on the user comparing every
group, so clients must ask for a full comparison and never show a partial fingerprint.

**Connect QR code.** The connect payload behind `https://connect.macro-deck.app/<base64 JSON>` carries an
optional `fingerprint`: the same value without spaces, 24 uppercase hex characters. It is additive, so
`payloadVersion` stays 2, and it is left out while the key cannot be loaded. The desktop app adds it from
`identityFingerprint` when it builds the code. The companion app checks a scanned host against it
automatically, so a device that took over the host's address cannot get its key pinned by a scan of the real
host's code.

**Key lifetime.** The key is created only when the file is absent, and renewed only when unprotecting it
fails with a cryptographic error; the unreadable file is kept as `host-identity.key.unreadable-<utc>`. An I/O
or access error never renews; a failed load is retried at most every five seconds and logged once until it
recovers. Loading and creating hold an exclusive handle on `keys/host-identity.key.lock`, so two
processes on one data directory never replace each other's key. Setting `DOTNET_SYSTEM_IO_DISABLEFILELOCKING` makes that handle
take no lock on Unix and removes this guarantee. `identity.issued` in `app_preference` records
that a key was ever issued: creating a key while it is set, or renewing one, raises a Security notification
telling the user to scan the QR code again on every paired device. The first key after an upgrade is silent. The flag and the notification are recorded after the key is already in use, with a
bounded retry, so a busy database never makes the key unavailable; a flag that cannot be read counts as
issued, so a new key is announced rather than kept silent.
The key travels in the Accounts backup group; the flag never does, and a restore from an archive without the
key keeps the current one.

## Consequences

- A renewal or a lost key invalidates every pin, visibly through the notification, and devices recover by
  scanning the QR code.
- This proves who answers at an address the app already accepted; it is not transport security. Someone who
  takes over the host's IP, or relays at an address the user accepted, is out of scope.
- The endpoint check fails behind NAT and port forwards; the app then relies on the address the user typed.
- The endpoint check does not apply to USB: ADB reverse forwards to `127.0.0.1` and the ports may differ, so a
  USB address is proven by key and nonce only, like a typed one.

## References

- The app side of the contract: `docs/integration/macro-deck-host.md` in the companion repository.
- Test vector shared with the app: `host/tests/MacroDeckHost.Tests.UnitTests/Auth/host-identity-vector.json`.
