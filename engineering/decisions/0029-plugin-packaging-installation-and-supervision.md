# ADR 0029: Plugins are ZIP artifacts, installed atomically and supervised by the host

Status: Accepted

## Context

Managed plugins need a portable install format, a versioned on-disk layout, compatibility checks,
integrity metadata, safe updates, rollback and persistent plugin data — and installation must never
leave a half-active version if extraction or activation is interrupted. They also need a host-owned
lifecycle: launch, authentication, liveness detection, graceful shutdown, restart policy and persistent
desired state, without repeated crashes creating an unbounded restart loop.

The host installer, the `macrodeck-plugin` CLI and third-party tooling all need the same answer to
whether a manifest or artifact is structurally valid. Keeping that logic inside host projects would
either force tooling to depend on the host or create validators that drift.

`manifest.json` fields are also "required" in more than one sense
([#611](https://github.com/Macro-Deck-App/Macro-Deck/issues/611)). A handful gate whether the host will
install and run a plugin. A larger set — description, icon, license, repository, compatibility,
publisher — is needed only to publish into the public ecosystem. Only the first category had a
machine-checkable definition; the rest existed as prose scattered across two documents, free to drift
from what a Creator Portal would eventually check.

## Decision

### The artifact is a ZIP, and activation is an atomic pointer swap

A `.macroDeckPlugin` is a ZIP whose root contains `manifest.json` and the payload. Installing extracts a
complete version into `plugins/<id>/versions/<version>/`; the installer and the supervisor read the same
manifest format. Activation atomically replaces `current.json`, so the supervisor always sees either the
previous complete version or the new one, and rollback uses the same mechanism.

Compatibility and dependency ranges use Macro Deck's own small explicit SemVer comparator grammar rather
than borrowing npm, NuGet or Cargo shorthand. Artifact signatures cover a deterministic digest derived
from security-relevant manifest data and declared file digests, never re-serialized JSON, so reformatting
a manifest does not invalidate a signature. Plugin-owned persistent data lives outside version
directories, so updates and rollback do not replace it. **Plugin packages do not run installer scripts.**

Hard compatibility violations can block installation; advisory metadata is surfaced as a warning rather
than pretending the host enforces a guarantee it does not.

### The format lives in a host-independent package

The install-time manifest model, the reader, the archive entry policy, structural limits and the signable
digest live in `MacroDeck.Plugin.Packaging`. The package defines what an artifact *contains* and whether
its structure is valid; the host keeps the installation lifecycle — staging, dependency resolution,
atomic activation, rollback, retention and supervision. The host and CLI consume the same readers rather
than maintaining parallel implementations, and the package must not depend on host assemblies.

`MacroDeck.Plugin.Hosting` keeps a deliberately small runtime manifest reader for the identity fields a
running plugin needs about itself, rather than acquiring the full installation model to read its own
metadata. A parity test pins the two to the same answers for the fields both model, so it cannot quietly
become a competing source of truth.

### Requirement categories are annotated in the published schema

Every manifest field carries `x-macrodeck-requirement`, valued `runtime`, `publication`, `recommended` or
`generated`. It is an annotation keyword, so every JSON Schema validator ignores it: nothing that
validated before stops validating, and the top-level `required` array is unchanged. The SDK reads those
annotations out of the schema, embedded as a resource, instead of restating the categories in C#, which
makes the schema the single source of truth for the SDK, the CLI and any out-of-repo tooling.

The categories feed three cumulative validation levels, `Development ⊂ Package ⊂ Publication`:

- **Development** is exactly what the host enforces at install time. It is the floor and **never becomes
  stricter**, so a manifest that installs today keeps installing on every future version.
- **Package** adds checks needing real packaged content — a declared entrypoint actually present, a valid
  multi-RID layout — plus every unsatisfied publication field as a *warning*.
- **Publication** promotes those warnings to errors.

Publication requirements are a validation *level*, not schema `required`, because a schema-aware editor
validates against `required` and would red-underline a manifest that installs and runs perfectly well
locally. Build instructions stay in `macrodeck-build.json`, outside the runtime manifest: a manifest
describes what a plugin *is*, never how to build it.

### The host supervises managed processes

The supervisor chooses an ephemeral loopback port before launch and passes it to the child, giving the
host a known loopback-only health endpoint without trusting an address the plugin reports.

Health uses two signals — the protocol session and the HTTP health endpoint. A failed probe alone can
degrade health but does not override an otherwise live session, and restarts require sustained evidence.
Shutdown is communicated through the protocol first, with process-tree termination only after the grace
period, which is what makes it portable. Automatic restarts use bounded exponential backoff and a restart
budget; exhausting it moves the plugin to a failed state until a manual start or a fresh host lifecycle.

Every launch receives a fresh bootstrap credential, discarded on every exit path. Managed versus
self-registering mode is explicit, never inferred from a missing field or connection state. Only desired
enabled/start state is persisted as configuration; health, failure state, ports and credentials remain
transient.

### A managed plugin does not outlive its host

Graceful shutdown only runs when the host is asked to stop and given time to finish. A host that is
killed instead - by its supervising bootstrapper after a timeout, by a crash, or by the operating system
- never reaches that path, and the plugin processes it spawned are reparented and keep running forever.
Nothing in the protocol helps: the plugin sees its socket drop, which is indistinguishable from a
transient disconnect, so it reconnects indefinitely instead of exiting.

The lifetime is therefore bound outside the protocol as well, wherever the platform offers a mechanism:
a Job Object with kill-on-close on Windows, and the bootstrapper's process group on Unix, which a
child inherits. Neither covers every case, so a managed plugin also watches the host process that
launched it and stops itself when that process is gone. It is told which process to watch, by id and
start time, rather than inferring it, because an orphan is not reparented predictably across platforms.
Self-registering plugins are never bound and never watched: they are independent processes the host did
not launch.

Because no mechanism covers a host that dies on every platform, the host additionally keeps a **process
journal**: for each managed launch it records the launch id, plugin id, process id and process start
time, and removes the record when the process exits. The journal is not runtime state the supervisor
consults about a running plugin - that remains transient. It exists solely so the next host start can
terminate the process trees a killed host left behind. Two properties make acting on it safe. A process
id alone is ambiguous after reuse, so a record whose start time no longer matches the live process is
discarded rather than killed. And the file names the host that wrote it, so a second host sharing a
configuration directory finds the owner alive and reaps nothing.

The shutdown budgets of the bootstrapper, the generic host and the plugin supervisor are ordered
deliberately, each strictly inside the next, so the layer that gives up first is always the innermost
one that can still report what happened.

## Consequences

- Installation and rollback cannot expose a partially extracted active version, and persistent plugin
  data survives upgrades and rollback.
- Package signing can become stronger without redefining the artifact container.
- Install tooling must validate archive paths, declared files, compatibility and manifest identity before
  activation.
- Host installation and external tooling validate artifacts through one implementation, and packaging can
  be inspected without loading the host. Changes to the package model are public NuGet and package-format
  compatibility changes.
- A field's requirement category can no longer drift between the SDK, the CLI and out-of-repo tooling,
  and a schema-aware editor still only checks `required`.
- `validate --artifact` defaults to Package rather than Development, so an artifact declaring an
  entrypoint the package does not contain now exits non-zero where it previously passed. The host's
  install-time behaviour is unchanged.
- Publication enforcement stays aspirational until the Creator Portal exists
  ([ADR 0042](0042-plugin-signing-and-trusted-publishing.md)); what ships here is the gate and the
  documented contract the Portal is expected to call it with.
- A hung or crashing plugin cannot restart indefinitely without reaching a terminal failed state, and
  self-registering development plugins remain independent of process supervision.
- A host that is killed rather than stopped no longer leaves managed plugin processes running
  indefinitely. Where the platform can bind a child's lifetime to the host's, the orphan never exists;
  otherwise the plugin stops itself once it sees its host is gone, and anything still left is reaped on
  the next host start.
- Terminating a recorded process id is only safe because the record carries a start time to match
  against and names its owning host, so a reaper that cannot confirm identity does nothing at all. Any
  future change to the journal has to preserve both properties, or it becomes a way to kill an unrelated
  process.
- The three shutdown budgets are now a single ordered relationship rather than three independently chosen
  constants. Changing any one of them is a change to that ordering, and a test asserts the chain across
  the Rust and C# sources rather than each constant in isolation.

## References

- [Issue #412](https://github.com/Macro-Deck-App/Macro-Deck/issues/412),
  [Issue #415](https://github.com/Macro-Deck-App/Macro-Deck/issues/415),
  [Issue #416](https://github.com/Macro-Deck-App/Macro-Deck/issues/416),
  [Issue #611](https://github.com/Macro-Deck-App/Macro-Deck/issues/611)
- [Manifest reference](https://docs.macro-deck.app/reference/manifest/),
  [Packaging guide](https://docs.macro-deck.app/cli/)
