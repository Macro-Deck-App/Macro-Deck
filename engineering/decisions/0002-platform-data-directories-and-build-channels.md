# ADR 0002: Data roots and build channels

Status: Accepted

## Context

Every writable path (`data/`, `database.db`, `config/`, `logs/`, `keys/`) was originally rooted at
`AppContext.BaseDirectory`. That breaks once the app ships as an installer: an auto-update replaces
the install directory and would wipe the user's data, and writing inside a signed macOS bundle breaks
its code-signing seal.

A development process also has to run beside an installed release without sharing its public port,
data directory, desktop identity, or loopback-port discovery file — and the binary directory varies
by build configuration, so "next to the binary" does not reliably isolate it.

Release versions already say whether a build is a Beta, through the canonical `-beta.N` prerelease.
A manually selected `Beta` build configuration would create a second source of truth that can
contradict the version.

## Decision

### Two build channels, not three

`BuildChannel` has exactly two values. Beta is inferred only from the version's prerelease and uses
the Production defaults, so a Beta and a stable install share a port, data root and desktop identity
and cannot run at the same time; Development can run beside either.

| Channel | Public HTTP / HTTPS | Data root | Loopback-port file | Desktop identity |
| --- | --- | --- | --- | --- |
| `Development` (default) | `7193` / `7194` | `.data` under the working directory | `macro-deck-host-development.port` | Development-only Tauri identifier and apphost |
| `Production` | `8193` / `8194` | Platform user-data directory, or the base directory for an unmarked publish | `macro-deck-host.port` | The installed identity |

Each channel reserves an adjacent HTTP/HTTPS pair. Development does not start at `8194` precisely
because that is Production's HTTPS default; one port number meaning two things across channels is the
confusion the pairing exists to avoid.

[`Directory.Build.props`](../../Directory.Build.props) validates the channel and generates the
Application layer's `BuildConfig`; the host and the Rust bootstrapper receive the same validated
value. This is independent of the MSBuild `Debug`/`Release` configuration, because developers need
optimised local builds and diagnostics-enabled release artifacts.

### The data root is resolved by a pure function

[`MacroDeckDataRootResolver.Resolve`](../../host/src/MacroDeckHost.Application/Paths/MacroDeckDataRootResolver.cs)
takes a `DataRootEnvironment` value, so it is unit-testable without touching the real environment. It
has to run before any configuration is loaded — it decides where configuration lives — which is why
its inputs are the environment and a marker on disk rather than a setting. Precedence:

1. `MACRO_DECK_DATA_DIRECTORY`, the explicit override (`MACRODECK_DATA_DIR` is the legacy spelling).
2. `MACRODECK_PORTABLE=1`, forcing the base directory.
3. `BuildChannel=Development`, giving `<working directory>/.data`.
4. The `.macro-deck-packaged` marker written into every published Production build, selecting the
   platform user-data directory: `%APPDATA%` on Windows, `~/Library/Application Support` on macOS,
   `$XDG_DATA_HOME` (else `~/.local/share`) on Linux, each plus `MacroDeck`.
5. Otherwise the base directory, preserving portable behaviour for an unzipped build.

`MacroDeckPaths` hangs every writable location off that root. `DatabaseMigrationsDirectory` stays
install-relative, because the Evolve scripts are shipped read-only assets rather than user data.

## Consequences

- Packaged installs survive auto-updates, and signed macOS bundles stay read-only at runtime.
- Development state is deterministic, disposable and gitignored at `<working directory>/.data`.
- A copied publish folder without the marker behaves as portable. This is intentional.
- Production packaging must pass `BuildChannel=Production` to both the host and the bootstrapper;
  omitting it makes a package look like a Development build.
- Windows installers stop only their own apphost, so either variant can update without locking the
  other's host.

## Alternatives considered

- **Always use the platform user-data directory, including in development.** Scatters throwaway state
  into system directories.
- **A third `Beta` build channel.** The version already determines Beta status, and duplicating it
  can drift.
