# ADR 0089: A development build can temporarily take over an installed plugin

Status: Accepted

## Context

Pairing and Developer-token enrolment refused any plugin id the installation catalog knows, the
session endpoint refused a registration session while the id had a live managed launch, and startup
revoked every registration for an installed id
([ADR 0028](0028-plugin-credentials-and-pairing.md)). The reasoning: a development build is unsigned,
cannot prove it belongs to the id's owner ([ADR 0042](0042-plugin-signing-and-trusted-publishing.md)),
and would receive the installed plugin's stored secrets under an identity that may be trusted
([ADR 0044](0044-plugin-and-store-trust-enforcement.md)).

The only way to debug an installed plugin was to uninstall it
([#881](https://github.com/Macro-Deck-App/Macro-Deck/issues/881)). Uninstall revokes the registration and
deletes the admitted tier, but keeps configuration, secrets, bindings and the data folder
([PluginInstaller.cs](../../host/src/MacroDeckHost.Infrastructure/Plugins/Installation/PluginInstaller.cs)).
A build paired after that uninstall already gets the same secrets, with no warning. The refusal made
debugging harder without protecting anything.

## Decision

### A takeover is an explicit, in-memory state of the installed id

- It starts only through interactive pairing while Developer Mode is on. For an installed id the
  desktop prompt asks for a takeover confirmation, separate from the plain approval, and warns that an
  unverified build gets the installed plugin's settings and stored credentials
  ([PluginPairingService.cs](../../host/src/MacroDeckHost.Application/Plugins/Pairing/PluginPairingService.cs)).
  As before, approval changes nothing: the takeover begins when the request is redeemed.
- Developer-token enrolment for an installed id still answers `409 PLUGIN_ALREADY_REGISTERED` with
  reason `plugin_installed`. A human never confirms anything on that path.
- The takeover lives only in memory, in a registry under
  [Plugins/Runtime](../../host/src/MacroDeckHost.Application/Plugins/Runtime/). It ends when the
  development registration is revoked (Revoke under **Paired plugins** on the Developer page, or
  uninstall), when Developer Mode is switched off, and on host restart. The Developer page lists the
  running build and ends the takeover; every place that shows the installed plugin as verified shows
  that a development build is active instead.

### Session admission is the single enforcement point

[PluginSessionsController](../../host/src/MacroDeckHost/Api/Controllers/PluginSessionsController.cs)
decides who may hold an installed id. While a takeover is active, a managed launch token is not
admitted. A registration session for an installed id is admitted only during an active takeover, and
the launch latch then does not apply. The condition is checked again after the session is inserted.
With that invariant, a supervisor race, a revoke or a restart cannot hand the id to the wrong
process, and a credential left over from a restart is refused before any reconciler runs.

### The supervisor pauses the installed instance

[PluginSupervisor](../../host/src/MacroDeckHost.Infrastructure/Plugins/PluginSupervisor.cs) stops the
managed instance with stop reason `DevelopmentTakeover`, terminates only its managed sessions, and does
not relaunch while the takeover lasts. The exit counts as a deliberate stop: it never ends in Failed and
never uses restart budget. Persisted desired state and the admitted trust tier are never written, so the
installed version resumes through normal reconciliation when the takeover ends. Redemption does not
wait for the process to exit. User Start and Restart are refused during a takeover.

### Installation cannot outlast or bypass a takeover

Install and update of a taken-over id are refused until the takeover ends. Uninstall stays allowed and
ends it. Activation ends any takeover that raced it, and redemption re-checks that its takeover and
registration are still current after registering. A takeover therefore never outlives its credential.

### The SDK re-pairs once

If the host rejects a stored credential before the process has ever connected, the SDK pairs once and
overwrites the stored credential, but only when pairing is enabled, no enrollment token is set and the
store can save ([PluginConnectionHostedService.cs](../../sdk/src/MacroDeck.Plugin.Hosting/Transport/PluginConnectionHostedService.cs)).
A debug run started after a takeover ended or after a host restart therefore asks again without manual
cleanup. A revoke while connected stays fatal, so ending a takeover never prompts the same process
again.

## Consequences

- This raises the stake of the exposure [ADR 0028](0028-plugin-credentials-and-pairing.md) accepted.
  There, one approval gave a local process a fresh id with nothing stored. Here, one approval gives it
  an installed, possibly trusted plugin's stored secrets. The mitigations are Developer Mode (off by
  default), the separate confirmation that warns the build gets an installed plugin's data, the memory-only lifetime,
  and that uninstall already exposed the same data without any of these.
- [ADR 0044](0044-plugin-and-store-trust-enforcement.md) holds unchanged: a session under a taken-over
  id is a development session, is never reported as trusted, and never touches the persisted tier. While
  it lasts, the Store card and detail page replace their verified chip with a development-build marker,
  and the Developer page shows the running build as unverified.
- Amends [ADR 0028](0028-plugin-credentials-and-pairing.md): pairing for an installed id is allowed
  with a takeover confirmation, and a registration session can hold an installed id during a takeover.
- Amends [ADR 0029](0029-plugin-packaging-installation-and-supervision.md): the supervisor has an
  in-memory reason not to run an installed plugin, besides desired state and integrity failure.
- Old SDK builds do not re-pair. After a takeover ends they stop with an authentication error until
  `credentials.json` is deleted.
- Known limitation: on Windows, an uninstall during the stopped process's graceful wait (at most the
  manifest's 60 seconds) can fail to delete its files because they are in use. The installer reports
  the failure without side effects, and a retry succeeds.
- Pairing create for an installed id changed from `409` to `201` with Developer Mode on. Clients see
  that as a relaxation. The desktop REST fields are additive.

## References

- [Issue #881](https://github.com/Macro-Deck-App/Macro-Deck/issues/881)
- [Debugging plugins](../../docs/src/content/docs/guides/debugging.md),
  [Authentication reference](../../docs/src/content/docs/reference/authentication.md)
