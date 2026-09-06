# ADR 0040: Public listeners are a resolved endpoint set, secured by a per-installation local CA

Status: Accepted

## Context

The public port was fixed at build time and handed straight to Kestrel, so a user with a port conflict
had no way to change it. Making it configurable raised two problems the codebase had no answer for: the
port must be known before the DI container exists, because Kestrel binds before the host is built, and a
changed port cannot be applied to already-bound listeners — it needs a restart of the *whole*
application, host and shell. Nothing in the repository could restart anything; the shell treated every
host exit as a crash, and the host had no channel to the shell at all.

Macro Deck 3 then shipped without TLS, which
[ADR 0003](0003-loopback-trust-token-scopes-and-device-identity.md) recorded as a known limitation:
tokens travel the LAN in the clear and the auth cookies cannot carry `Secure`. Adding TLS raised three
more. The certificate has to be available at the same point in startup the port already was, and unlike
a port it is protected key material. A second public listener is a *third* listener overall, and the
trust predicate at the time read "local port is not the public port", so every port but one was
implicitly trusted as admin — and an `adb reverse` tunnel arrives from a loopback address. And a listener
now has a protocol as well as a port, while several consumers each rebuilt a URL from the port and a
hard-coded `http://`.

Finally, a self-signed **end-entity** certificate is not usable even once TLS is on: trusting a leaf
directly is not something iOS or Android offer a workable path for, and the leaf has to change whenever
DHCP hands the machine a new address, invalidating that trust again. The web client needs a genuinely
secure context, because a browser refuses to register a service worker or grant a wake lock without one,
and clicking through an interstitial does not produce one either.

## Decision

### The port is user state, and changing it is a host-signalled restart

The public port is persisted as an ordinary preference and read at startup over a raw read-only SQLite
connection, directly after the migration and before the host is built. `PublicPortSelector` resolves in
a fixed order: `MACRO_DECK_PORT`, then the persisted value, then the build default. The environment
variable is an explicit per-launch decision by whoever starts the process and must not be defeated by a
value stored months earlier. Configurable values are restricted to 1024–65535, and a value equal to the
loopback port is refused **at save time**, so the application never silently listens somewhere the user
did not choose.

`POST /api/host/restart` marks the intent, stops the host cleanly, and exits with code **86**, which the
shell recognises in its exit monitor and relaunches instead of reporting a crash, preserving the command
line, environment and data directory. Without a shell the endpoint reports the restart as unsupported
rather than shutting down something nothing would bring back. Unlike shutdown, restart is **not**
loopback-only: a setting needing a restart is reachable from the browser-served admin UI, so refusing it
there would leave that user unable to apply what they just saved. The deny-by-default admin policy is the
gate.

### The listeners are a resolved set, and TLS is one of their properties

The public listeners are modelled as a `PublicEndpointSet`: the configured port, a TLS mode (`Disabled`,
`Replace`, `Additional`), and the HTTP and HTTPS ports that actually exist. It is published once through
`ResolvedPublicEndpoints`, and every consumer — connection info, the QR payload, the OAuth redirect URI,
the adb tunnel target — derives its protocol and port from that one value; nothing reconstructs a scheme
of its own.

`HostListenerPlan` probes each requested port and publishes the *effective* set, so a port that cannot be
bound, or an HTTPS listener with no usable certificate, is dropped and nothing can advertise an endpoint
that is not answering. **A certificate that cannot be loaded is treated exactly like a failed bind.**
There is deliberately no fallback to plaintext on a port the user marked TLS-only, and no silent
regeneration of user-supplied material: minting a new certificate would change the identity every paired
device trusts, with nothing saying so until handshakes started failing. The host keeps running on
loopback, so the settings screen stays reachable.

The certificate is resolved **eagerly, before Kestrel is configured**, not through a certificate-selection
callback, which would let a port bind and then fail every handshake. Its private key is protected with
`IDataProtector` and stored under the keys directory; the certificate sits beside it unprotected, because
it is public material the user has to be able to copy into a phone's trust store. **The private key
travels in one direction only**: accepted by the settings endpoint, never returned, never logged (PEM
private-key blocks are in the redaction set as a backstop), never in an error message.

The loopback listener is configured unconditionally and never gets TLS — the desktop shell and the plugin
SDK both reach it over plain HTTP on `127.0.0.1`.

**Loopback trust became a positive match** as part of this: `IsTrusted` requires the local port to *be*
the loopback port, so a new listener is untrusted by construction rather than until somebody remembers to
extend an exclusion list.

### The certificate is issued by a per-installation local CA, and TLS is on by default

The host mints a root CA and signs its host certificate with it. Macro Deck still does not attempt ACME
or public-CA issuance — there is no domain to validate on a LAN address — but a *local* authority never
leaves the machine and exists so the certificate the user installs once can outlive the certificate the
listener serves.

- The root is RSA-2048/SHA-256, valid ten years, `CA:TRUE` with a path-length constraint of 0, and
  carries **no extended key usage**: an EKU on a root selects EKU-nesting chain building, which is well
  defined on Windows but inconsistent for a manually installed root on iOS. Name constraints were
  rejected — .NET has no builder for them, and a constraint excluding an address the machine later
  acquires fails silently on some platforms only.
- The host certificate is valid 365 days, clamped to the root's expiry, with subject alternative names
  for `localhost`, both loopback addresses, every reachable IPv4 address, the machine name and
  `<machine>.local`. The multicast DNS name is the only name that survives a DHCP change, so an installed
  web app keeps its origin — and with it its storage and its session — across one.
- It is reissued automatically when it expires, nears expiry, or does not name an address the machine
  currently has. A name left over from an address the machine no longer has does **not** trigger a
  reissue, which would fire every time a VPN or a dock appears. Reissuing never touches the root, so no
  device trusts anything again.
- Only the root's public certificate is ever served, from an anonymous endpoint. The root's private key
  travels in backups so a restore preserves every device's trust.
- TLS is **on by default** in `Additional` mode; only an explicit `false` keeps HTTPS closed.
- While both listeners are up, plain HTTP is advertised **first** in the connection payload and QR code.

## Consequences

- The public port is user state that feeds the auth trust boundary, so a configured port is refused
  implicit trust exactly as the build default was. The shell keeps no copy of the setting — its
  port-conflict message reads the port out of the host's own bind failure, so the two cannot drift.
- Exit code 86 is a cross-language contract, pinned by a test on both sides. A configured port that
  cannot be bound leaves the host unable to start and the settings UI unreachable; the startup error
  names `MACRO_DECK_PORT` as the way back in. Picking a different port automatically was rejected: it
  would make existing clients, bookmarks and QR codes unreliable.
- **A device must be able to fetch the root over a connection it does not trust yet**, which is why HTTP
  stays up in the default mode. In `Replace` mode the download is only reachable through the browser's
  own interstitial, so devices should be set up before switching.
- **The root download is trust-on-first-use.** Anyone on the network could serve a different CA, and
  installing one grants that party the device's trust for every site. Software cannot close this from the
  browser side; it is closed by making verification possible — the root's SHA-256 fingerprint is shown in
  the desktop app, which reaches the host over loopback and is authoritative by construction, and the
  setup wizard shows the same fingerprint for comparison.
- **HTTP-first ordering is an upgrade concession.** Turning TLS on by default would otherwise start
  advertising, to every existing installation, an endpoint whose authority no device has installed — and
  the Android companion app is out of this repository, so its endpoint selection cannot be verified here.
- iOS needs two steps (install the profile, then enable full trust separately) and Android since 11 no
  longer launches the importer from a download, so the wizard has to walk through both. Android's native
  companion app will not trust a user-installed CA unless it declares a network security config, which is
  outside this repository — and the USB tunnel keeps targeting the plain HTTP listener.
- `Replace` mode changes the OAuth redirect URI to `https://127.0.0.1:<port>/…`. That value reaches
  plugins through the config-flow SDK, but its type and shape are unchanged and it was already
  user-variable through the port. Some providers refuse `https` on a loopback address at all, which is
  why `LocalClientEndpoint` names plain HTTP while one exists.
- Cookies are not port-scoped, so in `Additional` mode a session started over HTTPS is not carried to the
  HTTP endpoint and the user signs in again there. The asymmetry only ever refuses to downgrade a secure
  session.
- The connect payload behind the QR code carries an endpoint list with a per-entry flag rather than one
  port and one global flag, and gained a payload version at the same time.
- The firewall rule needs no change: it is program-scoped with no port filter, deliberately, so a second
  listener on the same executable is already covered.
- An installation whose certificate-source marker is lost keeps its old certificate, because the marker
  falls back to "custom" and custom material is never replaced. That is the safe direction, at the cost
  of the user having to press reissue.
- The HTTPS port now always binds, so it gained its own environment override for the same reason the
  public port has one: two checkouts on one machine would otherwise contend for it.

## Alternatives considered

- **A global Kestrel HTTPS switch.** Would put TLS on the loopback listener too, breaking the desktop
  shell and every plugin that talks to `http://127.0.0.1`, and cannot express "HTTP and HTTPS side by
  side".
- **Falling back to HTTP when the certificate fails.** A silent downgrade of a security setting the user
  explicitly turned on.
- **Storing the TLS key through the secret service.** It is SQLite-backed and therefore unavailable
  before the DI container exists, and it exposes an archive export that would sweep the private key into
  profile backups.
- **Keeping the negative trust clause and excluding both public ports.** Stays correct only as long as
  every future listener is remembered.

## References

- [Issue #345](https://github.com/Macro-Deck-App/Macro-Deck/issues/345),
  [Issue #796](https://github.com/Macro-Deck-App/Macro-Deck/issues/796)
