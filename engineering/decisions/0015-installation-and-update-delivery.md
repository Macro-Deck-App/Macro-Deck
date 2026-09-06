# ADR 0015: Installation and update delivery are bounded by each platform's ownership model

Status: Accepted

## Context

The bootstrapper carries installation and updates ([ADR 0006](0006-tauri-bootstrapper-is-the-installed-entry-point.md)),
and each platform constrains what it is allowed to do.

**Linux.** Tauri's updater produces exactly one Linux artifact, the AppImage. Adding native DEB and RPM
packages ([#271](https://github.com/Macro-Deck-App/Macro-Deck/issues/271)) meant most Linux users would
no longer run the artifact the in-app updater knows how to replace. Writing a new version over files
`dpkg`/`rpm` owns, under `/usr`, would need elevation the app has never asked for and would bypass the
package database, leaving the next `apt upgrade` free to reinstall the old version over it.

**Windows.** `installMode` is `currentUser`, which is what lets a non-admin install Macro Deck and what
keeps the per-user updater working without a UAC prompt on every update. But the host binds a public
listener, and the first time it does Windows shows its own firewall prompt naming the *host executable*
rather than Macro Deck — and a user who cancels it gets a silent **block** rule
([#345](https://github.com/Macro-Deck-App/Macro-Deck/issues/345)). Creating a firewall rule requires an
administrator token.

**macOS.** Running straight off a mounted DMG produced a fully working app that saved its settings onto
a read-only volume ([#355](https://github.com/Macro-Deck-App/Macro-Deck/issues/355)). Gatekeeper adds a
second case: opening a freshly downloaded notarized DMG's app runs it *translocated*, from a read-only
copy that Gatekeeper deletes on the next reboot. macOS mode bits on a DMG lie about writability, so a
permissions check alone cannot detect either.

**Update channels.** The channel was a build-time fact baked into `tauri.conf.json`, so a stable user
could not try a beta and a beta user could not leave without reinstalling
([#272](https://github.com/Macro-Deck-App/Macro-Deck/issues/272)). Swapping the endpoint at runtime is
not enough on its own: a stable release is written to both the `latest` and `beta` manifests, but a
fresh beta is written only to `beta`, so between a beta and the stable release that follows it, `beta`
can be *older* than `latest`. Tauri's `UpdaterBuilder::endpoints` looks like it solves this and does
not — `check()` returns the first endpoint that parses, never the highest version.

**Update behaviour.** The periodic check had one behaviour: download in the background and ask to
restart once the download finished ([#715](https://github.com/Macro-Deck-App/Macro-Deck/issues/715)).
That spent bandwidth and extended trust to the feed on every tick without asking.

## Decision

### The release ring is version-derived

A Beta package polls `beta-<target>.json`; a stable package polls `latest-<target>.json`. A beta
release publishes only the beta manifest; a stable release publishes **both**, so Beta installations
graduate to it automatically while stable installations never receive a beta.

### The channel and the mode are bootstrapper-owned preferences

Both are stored as bare tokens in files (`update-channel`, `update-mode`) inside the same
`app_config_dir()` the loopback port uses, and both are **re-read on every call** so a change takes
effect on the next check without a restart. They live in the bootstrapper because they must be readable
before the host is up, and they decide whether and which signed release metadata the shell fetches — the
host has no role in update checking at all.

The channel's first-launch default follows the running version: Beta if the baked release version is a
prerelease, Stable otherwise. It reads `updater::current_version`, never the package version, because a
beta RPM is bundled at a mapped stable version and reading that would misclassify it.

The mode is `off` / `notifyOnly` / `automatic`, and `periodic_action(mode, install_strategy)` is
evaluated **before any feed is queried**, so Off contacts nothing rather than merely suppressing the
result. The shipped default is Notify only, for new and existing installs alike; there is no migration
preserving the previous background-download behaviour. Manual checks never consult the mode — Off means
no *automatic* checks, not no updates.

### Channel resolution compares candidates, it does not fail over

The baked endpoint is a **seed**: `feed_urls_for` derives both feed URLs by rewriting a leading
`latest-`/`beta-` token on its last path segment, as pure string manipulation so `{{target}}` and any
query survive. Anything that does not look like a channel feed falls back unchanged.

`resolve_update` is the single resolver every check path goes through, including the install path,
which re-resolves rather than reusing an earlier result so it always installs what was offered. Stable
queries only `latest`; Beta queries `latest` **and** `beta` through independent `Updater` instances and
ranks every candidate by `(SemVer, is-stable)`. The `is-stable` tiebreaker only matters on an exact
version tie, which is what makes a stable release beat the beta it supersedes.

A missing manifest on `beta` is ordinary — a platform that never had a beta has no such file — but every
release publishes a `latest` manifest for every platform it builds, so a missing one there is a real
problem and must not be reported as "up to date". When one feed is unusable and the other answered, the
UI is told through a `partialCheck` note rather than being left to guess; only when every feed fails
does the check report an error.

Signature verification is unchanged and still happens at download time inside `Update::download` /
`Update::install`. The resolver's own check is narrower — it discards a candidate with a blank signature
before it is ever offered — and must never be relied on as real verification.

### Linux updates are notification-only

For AppImage, DEB and RPM alike. `check_once` branches on `install_strategy()` — computed from
`std::env::consts::OS` rather than `#[cfg(target_os)]`, so both branches compile and are unit-tested
everywhere — and on Linux shows a dialog that opens the download page. It never downloads, never stops
the host, and never restarts the app. `install_update` refuses on Linux before it even queries the feed,
and the permission backing it is scoped in the ACL to Windows and macOS, so a Linux build cannot invoke
it. `UpdateCheckResult` carries `installStrategy` on the wire so the UI renders the right affordance
without detecting the platform itself. The DEB and RPM carry no `.sig` and are referenced by no channel
file; their integrity is published as SHA-256 checksums.

### Exactly one Windows installer step elevates

`installer/firewall.nsh` builds a single `cmd.exe` command that deletes any rule scoped to the host's
program path and, for an add, recreates it, launched with `ExecShellWait "runas"`. At most one UAC
prompt on an interactive install and one on an interactive uninstall, always preceded by an explanatory
dialog — the UAC prompt itself would only ever say "Windows Command Processor". The step is skipped on
silent, passive and updater-driven runs, and failure is never fatal anywhere: a firewall rule is a
convenience, and an install that failed because someone declined a prompt is strictly worse than the
native prompt this replaces.

The rule is program-scoped rather than port-scoped, because the public port is overridable and a
port-pinned rule would silently stop matching. It covers all three profiles, TCP only, and is matched
for delete by program path rather than display name — Windows can leave a *block* rule for a program a
user once dismissed, and block wins over allow, so a name-scoped delete would leave a fresh allow rule
silently overridden for exactly the users this exists for.

Any future installer step wanting elevation must revisit this rather than treat "elevate just the one
command" as a pattern to extend casually.

### macOS runs only from an installed bundle

`install_state::classify` decides from the running executable's path and a real create-then-delete write
probe which of three states applies: `Installed`, `MountedImage`, or `Translocated` (a path with
`AppTranslocation` as a whole path *component*). Translocation is checked first, because the translocated
path is not under `/Volumes` at all and is the common first-run case. A writable location under
`/Volumes` is still `Installed`, so this gates a read-only mount rather than every removable disk.

Startup gates on that before creating the tray, starting the host, or opening any window: a launch that
fails it shows only a "Move to Applications" / "Quit" prompt. Accepting copies the bundle with
`ditto --noqtn`, which preserves the signature and stapled ticket while dropping quarantine so the
installed copy is not translocated again, swaps it in with same-volume renames, and restores from a
backup if the final rename fails. A detached process then waits for this one to exit before opening the
installed copy, because single-instance would otherwise make that launch exit immediately.

## Consequences

- Linux users update through their own package manager or by replacing the AppImage — never an elevation
  prompt, never an in-place write Macro Deck performs. The experience is intentionally less smooth than
  on Windows and macOS; that is the cost of respecting each distribution's ownership model. If signed APT
  and DNF repositories ship later, the package manager should own delivery for those installs.
- Any install can move between Stable and Beta from Settings, and a stable release is never hidden from a
  Beta install even when the beta feed has gone stale. A Beta check makes up to two HTTP requests instead
  of one, which is immaterial against a 6-hour interval.
- The update channel is fully decoupled from which installer was downloaded, and orthogonal to
  `BuildChannel` ([ADR 0002](0002-platform-data-directories-and-build-channels.md)). There is no third
  "beta build channel".
- Beta packages published before client-side resolution existed still poll `latest-<target>.json`. They
  receive the next stable release but intentionally not an intermediate beta, because a static legacy
  endpoint cannot distinguish them from stable clients.
- Existing installs get quieter, consent-driven update defaults on upgrade, trading a small amount of
  proactivity for never fetching a payload before consent.
- The Windows firewall fix applies to fresh installs and manual re-runs of `setup.exe`, not
  retroactively: an auto-update skips the step, so a user who only ever auto-updates keeps whatever they
  answered at Windows' own prompt. Closing that would need a runtime check inside the running app.
- A declined UAC prompt on uninstall orphans the rule. It is inert once Macro Deck is gone.
- On macOS the setup screen can only appear from an installed copy. There is no `~/Applications`
  fallback: if the bundle cannot be resolved or the move fails, the user is told to drag it themselves.
  Exercising the translocation branch for real needs a signed, notarized, browser-downloaded build, so it
  stays a manual release check.

## Alternatives considered

- **Elevate and install in place on Linux.** Bypassing the package database is a correctness and
  security regression, not a convenience.
- **Ship only the AppImage on Linux.** Keeps a uniform updater story but forgoes native packages many
  distributions expect.
- **A server-side, channel-aware endpoint.** The release feed is static JSON on R2; adding request-time
  logic means operating a service to answer a question the client can answer from two files it already
  has URLs for.
- **Both feeds in one `Updater`'s endpoint list.** That list is a failover list, not a comparison.
- **Switch Windows to `perMachine`.** Trades one narrow, skippable prompt for an unconditional one on
  every install and update, and breaks the per-user updater's no-elevation assumption.
- **Let macOS run in place and only refuse to save.** A half-working app that silently loses data on
  eject is worse than refusing to start.
- **Silently copy to Applications without asking.** Moving an app into a system-wide location without
  consent is surprising, and would orphan a deliberate previous install elsewhere.
