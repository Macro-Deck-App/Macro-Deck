# ADR 0085: Compact connect link

Status: Accepted

## Context

The connect QR code in the desktop app's network panel encoded `https://connect.macro-deck.app/` plus
base64 of a JSON object (payload version 2). Field names, base64 and a mixed-case string forced QR byte
mode: a host with two endpoints needed QR version 11 at error correction L. Dense codes scan slowly or not
at all on older phone cameras and from a distance. The link is read by the companion app and by the page
behind connect.macro-deck.app, both outside this repository, and installed apps keep reading it for years.

## Decision

**Payload version 3 is a small binary record written as decimal digits.** Two bytes become five digits,
so the path is pure QR numeric mode. Base45 (RFC 9285) was measured as the same QR version, but its
alphabet contains space, `/`, `%` and `$`, which break a URL path, camera link detection and percent
decoding. The layout, decoder rules and a conformance vector are in
[connect-link.md](../api/connect-link.md).

**The prefix stays lowercase.** Android intent filters match scheme and host case-sensitively, and
whether scanners lowercase an uppercase URL before dispatching it is unverified. Uppercase would save one
QR version (5 instead of 4 for the reference host) and is not worth that risk.

**The host UI emits only version 3.** No version 2 link is kept for a transition. The companion app and
the connect.macro-deck.app page must both read version 3 before a host release that contains this change.

**The record carries no application version and only a digit token.** The version is available after
connecting. The token field holds the six digit pairing code (ADR 0083); carrying an enrollment token
would need a new payload version.

## Consequences

- The reference host (two IPv4 endpoints, pairing code) drops from QR version 11 to 5 at error
  correction L.
- Companion apps released before version 3 support cannot read a code from an updated host.
- Fields are length-prefixed and the optional identity fingerprint is recognised by the bytes that remain,
  so any new field needs payload version 4 rather than a flag.
- The host reports IPv4 addresses only. The IPv6 address type is defined for readers, and the desktop UI
  skips any address it cannot describe until the host reports IPv6.
