# ADR 0028: Plugin credentials are launch tokens, and development plugins pair interactively

Status: Accepted

## Context

Managed plugins and self-registering development plugins have different lifecycle and persistence
needs, so the host has to define who issues credentials, how long they live, what survives a restart,
how revocation works, and what the `plugin` scope grants.

The first answer for development was a reusable Developer token with enrollment authority: the developer
created one in the desktop app, copied its plaintext into an environment variable, ran the plugin once
so it exchanged the token for a per-plugin secret, then deleted the token. Two problems followed
directly ([#588](https://github.com/Macro-Deck-App/Macro-Deck/issues/588)). A reusable, plaintext-bearing
credential existed on the developer's machine for every plugin they ever built, purely to bootstrap a
single exchange, and it commonly outlived its purpose because discarding it is an extra manual step. And
if the local credential file was lost while the host still held an active registration, re-enrollment
answered `409 PLUGIN_ALREADY_REGISTERED` forever, with no in-product way back to a working plugin.

## Decision

### Managed plugins receive launch-scoped bootstrap tokens

The host issues a cryptographically random token bound to the expected plugin and launch, valid for one
live session at a time. It can be reused by the same launch after a dropped session once the previous
session is no longer live, because the SDK reconnects with the credential it was given; a supervisor
restart receives a new one. Launch credentials and plugin sessions are memory-only and do not survive a
host restart, so an installed managed plugin keeps no reusable long-lived secret on disk.

### Pairing is the default development path; Developer tokens are the headless path

The plugin creates a short-lived **pairing request**, the desktop app shows an approval prompt, the user
approves, and the host issues the plugin-specific credential straight to the requesting process. Nothing
is created, displayed, copied or placed in a configuration file for this path. An explicitly supplied
enrollment token still takes precedence in the SDK's resolution order, which is what keeps CI and other
unattended harnesses from ever blocking on a human — Developer tokens, their `plugin:enroll` scope and
the registration endpoint are unchanged and fully supported, only demoted from default to headless.

**Proof of possession is PKCE-style and the host stores no secret.** The plugin sends only
`base64url(SHA-256(verifier))`; the host persists the challenge, never the verifier. A pairing request
that leaks in a log authorizes nothing by itself, which is why the request id is safe to place in a URL
and the verifier never is.

An unknown or pruned request id answers `200` with status `expired` rather than `404`, which keeps a
client's polling state machine total and keeps the endpoint from being an existence oracle. Every
redemption failure — unknown, expired, not approved, already redeemed, wrong verifier — answers `401`
identically; the status endpoint is how a plugin learns where it stands. A wrong verifier does not
consume the request; a successful redemption consumes it permanently.

Requests are memory-only, single-use, bounded and rate-limited. A create for a plugin id that already
has a live request is refused rather than silently replacing it: replacing would let any local process
cancel a prompt the developer can already see, or swap a hostile request in underneath their cursor
between looking and clicking.

**Approval happens only over the trusted desktop transport.** An admin bearer token on the network
listener is deliberately not sufficient — approval is a human looking at a specific prompt, not an API
call any admin-scoped caller could issue. Pairing additionally requires **Developer Mode**, off by
default, as a continuous condition: turning it off invalidates redemption of an already-approved request.

**Prompt fields are self-reported and labelled as such.** The executable path, process id and SDK
version are shown exactly as reported. Deriving them from the socket is platform-specific, unreliable
under sandboxing, and would still not authenticate anything — a hostile process can report any path it
likes regardless — so it would buy a false sense of verification. What the host does verify, and shows
as verified, is which listener accepted the connection.

**Secrets are minted at redemption, never at approval.** Approval only marks the request permitted.
Minting at approval would recreate the unrecoverable conflict this exists to remove: an approval that is
never redeemed — the plugin crashed, the host restarted — would silently invalidate a credential that was
still working, with no compensating action. Binding the mutation to redemption means an un-redeemed
approval leaves the previous credential untouched.

**Credential replacement is the recovery path for the old `409` dead end.** When the host already holds
a registration but the local file is gone, the prompt offers to replace the development credential, and
the user confirms that separately. On redemption the host rotates the secret in place and then
terminates the plugin's live sessions, so there is never an instant at which both secrets authenticate.

### Scope and reachability

Plugin protocol endpoints are local-machine only: they require loopback reachability and do not become
LAN-accessible merely because they are routed through the public listener. `plugin` is an independent
authorization scope — it does not imply `admin` or `client`, and neither of those authorizes plugin
protocol operations.

Revoking a Developer token also revokes registrations derived from it and terminates their sessions;
revoking one registration affects only that one.

## Consequences

- The default F5 loop needs no credential the developer creates, copies or deletes.
- The protocol descriptor advertises an optional `pairing` block. Its absence identifies a host that
  predates pairing, and the SDK falls back to telling the developer to use an enrollment token.
- The SDK creates a pairing request at most once per process lifetime and treats rejection, expiry and
  an unsupported host as fatal rather than retryable, so its reconnect loop cannot re-prompt forever.
  Developer Mode being off is the exception: the host refuses *before* a request exists, so nothing was
  put in front of a person and the SDK retries on its ordinary backoff.
- Credentials land at `<state>/<pluginId>/credentials.json` with owner-only Unix permissions, relying on
  the per-user profile ACL on Windows, and are **not encrypted at rest**.
- A plugin process must run on the same machine as the host, and credential rotation for a managed
  plugin happens through a new supervised launch rather than by mutating a running process's environment.

### Accepted residual exposure

Plugin endpoints are gated by `IsLocalRequest`, which has no port clause and therefore does not
distinguish an ordinary local process from a device reaching loopback through an `adb reverse` tunnel
([ADR 0030](0030-android-usb-connections-over-adb.md)).

What changes with pairing is the shape of what a hostile local process needs. Before, obtaining a plugin
credential required a Developer token — and a local process already has implicit admin over the loopback
transport under [ADR 0003](0003-loopback-trust-token-scopes-and-device-identity.md), so it could mint one
itself. After, the same process needs a human to approve a prompt while Developer Mode happens to be on.
That is a real dependency on user attention rather than a cryptographic boundary, bounded by Developer
Mode being off by default, approval being desktop-only, the prompt labelling unverified fields as such,
one live request per plugin id with two rate-limit buckets, and nothing being minted until the requester
proves possession. Closing the underlying gap means revisiting loopback trust itself, which is out of
scope here.

## Alternatives considered

- **Mint the secret at approval time.** Reintroduces the failure mode this exists to remove.
- **Derive the prompt's process identity from the socket.** Platform-specific, unreliable, and still not
  authentication; labelling the fields unverified is more honest than implementing verification that does
  not verify.
- **Let a new pairing request replace an existing one for the same plugin id.** Lets any local process
  substitute a request underneath the developer's cursor.
- **Answer `404` for an unknown request id.** Turns the status endpoint into an existence oracle and
  forces a fifth polling outcome.
- **Remove Developer tokens entirely.** CI has no human to approve a prompt.

## References

- [Issue #411](https://github.com/Macro-Deck-App/Macro-Deck/issues/411),
  [Issue #588](https://github.com/Macro-Deck-App/Macro-Deck/issues/588)
- [Authentication guide](https://docs.macro-deck.app/sdk/authentication/)
