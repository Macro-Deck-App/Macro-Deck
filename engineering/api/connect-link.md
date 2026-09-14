# Connect link

The QR code in the desktop app's network panel encodes a connect link that the companion app and the page
behind connect.macro-deck.app read. It has no reader in this repository, so this page is the reference for
readers elsewhere. The encoder is `encodeConnectLink` in
[connection-panel.component.ts](../../ui/angular/projects/desktop-ui/src/app/components/shell/connection-panel/connection-panel.component.ts),
and the conformance vector below is pinned by its
[spec](../../ui/angular/projects/desktop-ui/src/app/components/shell/connection-panel/connection-panel.component.spec.ts).
When they disagree with this page, the spec wins. The decision is [ADR 0085](../decisions/0085-compact-connect-link.md).

## Link

`https://connect.macro-deck.app/<digits>`. Readers match scheme and host case-insensitively.

The digits encode a byte string: each pair of bytes `b[2i]`, `b[2i+1]` is the number
`b[2i] * 256 + b[2i+1]`, written as five zero-padded decimal digits. An odd last byte is written as three
zero-padded digits.

## Payload version 3

All multi-byte numbers are big-endian.

| Field | Size |
| --- | --- |
| Payload version, `3` | 1 byte |
| Instance name length `n` | 1 byte |
| Instance name, UTF-8 | `n` bytes |
| Endpoint count | 1 byte |
| Endpoints | see below |
| Token length `t` | 1 byte |
| Token, ASCII digits (the pairing code), empty when there is none | `t` bytes |
| Identity fingerprint, optional: the first 12 bytes of SHA-256 over the host identity key ([ADR 0086](../decisions/0086-host-identity-key.md)) | 12 bytes |

Each endpoint:

| Field | Size |
| --- | --- |
| Address type: `0` IPv4, `1` IPv6, `2` hostname | 1 byte |
| Address: IPv4 4 bytes; IPv6 16 bytes in network byte order; hostname a 1-byte length then ASCII | varies |
| Port | 2 bytes |
| Flags: bit 0 set means TLS, other bits are reserved and zero | 1 byte |

Endpoints keep the host's order; plain HTTP comes first while both listeners are up (ADR 0040).

## Reader rules

A reader rejects the link as malformed when:

- the digit count divided by five leaves a remainder other than 0 or 3, or a character is not a digit;
- a five-digit group is above 65535 or the three-digit tail is above 255;
- the payload version is not one it knows;
- a length runs past the end of the data, or a name or hostname is not valid UTF-8;
- after the token anything other than exactly 0 or exactly 12 bytes remains;
- an address type is unknown.

## Conformance vector

Instance name `Companion test host`, endpoints `192.168.1.10:8193` plain and `192.168.1.10:8194` TLS,
pairing code `482915`.

Bytes (45):

```
03 13 43 6F 6D 70 61 6E 69 6F 6E 20 74 65 73 74 20 68 6F 73 74 02
00 C0 A8 01 0A 20 01 00
00 C0 A8 01 0A 20 02 01
06 34 38 32 39 31 35
```

Link:

```
https://connect.macro-deck.app/00787172632801624942269912819229797295560829628531296980019243009025920025600192430090259200513015881438614641053
```

With the identity fingerprint `3208 E004 6ED3 EE6B 4E75 1027` of the shared host identity test vector
([host-identity-vector.json](../../host/tests/MacroDeckHost.Tests.UnitTests/Auth/host-identity-vector.json))
appended after the token, the same host gives these bytes (57):

```
03 13 43 6F 6D 70 61 6E 69 6F 6E 20 74 65 73 74 20 68 6F 73 74 02
00 C0 A8 01 0A 20 01 00
00 C0 A8 01 0A 20 02 01
06 34 38 32 39 31 35
32 08 E0 04 6E D3 EE 6B 4E 75 10 27
```

Link:

```
https://connect.macro-deck.app/00787172632801624942269912819229797295560829628531296980019243009025920025600192430090259200513015881438614641136180227201134542542747029968039
```
