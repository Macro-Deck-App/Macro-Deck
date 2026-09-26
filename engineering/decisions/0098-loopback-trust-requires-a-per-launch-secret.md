# ADR 0098: Loopback trust requires a per-launch secret

Status: Accepted

Partially supersedes [ADR 0003](0003-loopback-trust-token-scopes-and-device-identity.md) (the credential-less
loopback trust) and amends the adoption rule of
[ADR 0006](0006-tauri-bootstrapper-is-the-installed-entry-point.md).

## Context

ADR 0003 made the desktop UI admin on transport facts alone: the loopback listener's port, a loopback
remote address, a loopback `Host` header, and no cross-origin browser markers. It rejected a bootstrap
token as carrying "the same local-attacker exposure", which holds for a hostile process running as the
same user and for nothing else. TCP loopback carries no caller identity, so every other local caller that
is not a browser page was admin too:

- another OS account on a shared machine;
- local software that fetches URLs on someone's behalf (webhook testers, link previewers, dev proxies),
  turning any SSRF into admin, including claiming the first-run account;
- sandboxed or low-privilege processes and containers that only have network access;
- a browser in which someone typed the loopback URL (`Sec-Fetch-Site: none`).

The bootstrapper also adopted any listener on its persisted port that answered `trusted: true`, so a
process squatting that port could have received the desktop window and its shell bridge.

Client certificates were considered and rejected: the system webviews support them poorly and
differently per platform, the loopback listener would need a self-signed server certificate the webview
must be told to accept, and a private key held by the bootstrapper protects nothing a random secret held
by the bootstrapper does not. Encryption on loopback defends against no one who could not already read
the process.

## Decision

**Loopback trust requires possession of a 256-bit secret** in addition to the existing transport checks,
which all stay.

- **Who holds it.** The bootstrapper generates a new secret for every host launch, persists it owner-only
  in its config directory (only for adopting the host after a bootstrapper restart), and hands it to the
  host through the environment, never argv. The host reads it once and removes it from its environment
  before it spawns anything, so plugins do not inherit it. A host started without one (development,
  tooling, headless) generates its own and writes it owner-only to `config/loopback-secret` under its
  data root, after the single-instance check.
- **How it is presented.** Non-browser callers send it as a header. The desktop window cannot put a
  header on navigations, media loads or its WebSocket handshake, so it holds a cookie instead: the
  bootstrapper mints a single-use code valid for 60 seconds, the window enters through
  `/api/auth/loopback-session`, and the host sets an HttpOnly, `SameSite=Strict` session cookie named per
  listener port whose value is an HMAC of the secret. Being derived, it survives host restarts under the
  same secret and needs no server state; the browser-origin check still applies to it.
- **Every MAC is domain-separated** by a fixed label (session, code, proof), so no input an attacker
  chooses for one can produce another.
- **The bootstrapper never sends the secret to a listener it has not verified.** Readiness, adoption and
  every authorized call first challenge the port with a random nonce at `/api/auth/loopback-proof`, which
  answers with an HMAC of it and reveals nothing. A host from before this decision answers `trusted: true`
  without any credential; the bootstrapper stops such a host and starts its own.
- **A window that lost its session asks for a new one.** Inside the shell, an untrusted status or a 401
  on a trusted session makes the UI ask the bootstrapper to reload the window through a fresh code, at
  most once per 30 seconds, after which the ordinary setup and login paths apply. A password login on the
  loopback listener gets its own WebSocket ticket class, bound to that listener and carrying only the
  token's scope.

## Consequences

- Other OS accounts, SSRF through local tools, sandboxed processes and typed loopback URLs no longer get
  admin. A hostile process running as the same user still does: it can read the persisted secret or the
  bootstrapper's memory. That includes WSL2, which runs with the Windows user's file access. That limit
  is stated, not solved.
- Local tooling needs the secret: the Angular dev proxy reads the dev host's secret file per request, the
  E2E workflow generates one secret for the host and Playwright, and curl needs the header. See
  [the development setup](../development/setup.md).
- If the persisted secret is deleted while the host keeps running, a restarted bootstrapper can neither
  adopt nor stop that host and reports that the host could not start; quitting the leftover process fixes
  it.
- The cookie goes to every `127.0.0.1` port the webview requests, as the per-listener access cookie
  already does. The webview loads no other loopback origin today; a feature that renders a user- or
  plugin-supplied loopback URL there would hand the cookie to that listener.
- The window's session rests on each system webview storing a cookie set on a redirect. A webview that
  dropped it would leave the window untrusted, and first-run setup could not complete there.
- Plugins never had a legitimate use for loopback admin and do not get it; pairing still labels requests
  by the listener they arrived on.

## References

- [ADR 0003](0003-loopback-trust-token-scopes-and-device-identity.md),
  [ADR 0006](0006-tauri-bootstrapper-is-the-installed-entry-point.md),
  [ADR 0028](0028-plugin-credentials-and-pairing.md), [ADR 0062](0062-ui-realtime-transport.md)
