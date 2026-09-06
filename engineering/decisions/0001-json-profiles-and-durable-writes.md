# ADR 0001: Profiles are JSON files, written durably and recovered on read

Status: Accepted

## Context

A profile owns a folder tree, and folders own the widgets rendered on the deck. That aggregate is
large, deeply nested, edited constantly, and read on almost every request. Normalising it into SQLite
tables would spread one logical edit across many rows and force a migration for every widget-shape
change.

The first version of this decision wrote each profile with a plain temp-and-rename and called that
corruption-safe. It was not: a completed `File.Move` does not mean the bytes reached physical
storage, so a power loss could publish an empty or truncated file. The read path skipped the
unreadable document, and the host then created a fresh "Default Profile" when that left zero — a
factory reset with no error anywhere, reported by a beta tester after a power outage
([#647](https://github.com/Macro-Deck-App/Macro-Deck/issues/647)). Twelve writers shared the idiom
and none kept a previous version.

## Decision

**One JSON file per profile**, at `data/profiles/<id>.json`, with the database reserved for small
key-value data (variables, secrets, integration config, preferences, auth tables). Scripts and
automations later adopted the same shape.

[`ProfileCache`](../../host/src/MacroDeckHost.Infrastructure/Caching/ProfileCache.cs) is the single
in-memory aggregate store and the only writer; handlers read from it rather than from disk. Every
mutation persists the whole owning profile as a unit.

Integrations can contribute read-only virtual profiles through `IProfileProvider`; the registry
prefixes their ids as `integrationId::localId` so they cannot collide with stored GUIDs.

**All host JSON persistence goes through
[`DurableJsonFile`](../../host/src/MacroDeckHost.Infrastructure/Persistence/DurableJsonFile.cs).** A
hand-rolled temp-and-rename is a defect.

- **Write.** Serialize to bytes first, so a serializer failure never touches a file. Write the temp
  file, `Flush(flushToDisk: true)`, close, rotate the current primary to `<name>.bak` where backups
  apply, then rename the temp over the primary.
- **Backups** are kept only for irreplaceable user data — profiles, scripts, automations, user
  variables, integration state. Derived state (icon packs, plugin runtime state, capability
  snapshots) gets the durable write and recovery but no `.bak`, because it can be rebuilt.
- **Recovery on read.** A valid primary is used as is. Otherwise `<name>.tmp` then `<name>.bak` are
  each fully deserialized before anything on disk is touched; the damaged primary is moved to
  `<name>.corrupt-<utc>` first, then the candidate is republished as its own **bytes**, never a
  re-serialization of the parsed model, so a legacy document keeps members the current type does not
  model. If nothing loads, every file is left where it is and the loss is reported.
- **A validated `.tmp` outranks the `.bak`.** A failed write deletes its own temp file, so a
  surviving `.tmp` means the process died mid-write and is never older than the backup.
- **Recovery never rotates a backup**, because whatever occupied the primary slot was corrupt.
- Recoveries are surfaced as user notifications, not only logged, and unreadable is distinguished
  from absent — the host no longer manufactures a default profile when profiles existed but could
  not be read.

## Consequences

- Profiles are portable, diffable and hand-inspectable, and no schema migration is needed when a
  widget shape changes.
- The cache, not disk, is authoritative at request time; code must go through `IProfileCache` /
  `IFolderCache` rather than reading files.
- A power loss now costs at most the most recent change to one document.
- Reads mutate: a load can repair the primary, delete a stale temp and create a `.corrupt-*` copy, so
  inspecting a data directory by starting the host is no longer a read-only act. `PluginInstallationCatalog`
  opts out with `repair: false`, because discovery runs concurrently with the installer.
- Every save pays an `fsync`, and profile saves a second rename. If that becomes a problem the answer
  is to debounce saves, not to drop the flush.
- One host instance per data directory. This was always assumed; recovery makes a second instance
  destructive rather than merely lossy.

### What durability does not cover

- **No directory fsync.** .NET cannot open a directory as a `FileStream`. On ext4 `data=ordered`,
  APFS and NTFS the data-before-rename ordering still holds, so the visible outcome is the old file
  or the new one, never a torn one.
- **macOS `fsync` does not flush the drive's write cache.** `F_FULLFSYNC` would; .NET does not expose
  it, and a P/Invoke was judged disproportionate.
- **`File.Replace` is deliberately unused.** It throws when the destination is missing and its Unix
  implementation is copy-then-rename. Two same-volume `File.Move` calls behave identically on all
  three platforms.

## Alternatives considered

- **Normalized SQLite tables for folders and widgets.** Fragments one logical edit across rows and
  forces a migration for every widget-shape change.
- **One JSON blob column in the database.** Gives up portability and inspectability while keeping the
  database's operational weight.

## References

- [Issue #647](https://github.com/Macro-Deck-App/Macro-Deck/issues/647)
- [ADR 0002](0002-platform-data-directories-and-build-channels.md) locates the `data/` subtree.
