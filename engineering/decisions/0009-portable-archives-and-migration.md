# ADR 0009: Portable archives, and migration from other applications

Status: Accepted

## Context

Profiles, folders and widgets reference icons, secrets, navigation targets and other ids that are local
to one installation, so copying their raw JSON is not a portable backup or sharing format. Secret export
also has to be safe by default.

Separately, Macro Deck 3 could import its own archives but had no way to take over a Macro Deck 2
installation, so everyone upgrading rebuilt every button by hand
([#365](https://github.com/Macro-Deck-App/Macro-Deck/issues/365)). Two things about that source format
shape the answer: Macro Deck 2's actions are Newtonsoft `$type` strings naming a plugin assembly and an
opaque per-plugin blob, so translating one needs knowledge that lives with the corresponding Macro Deck 3
integration; and its plugin credentials are encrypted under the Windows `MachineGuid` of the machine
that wrote them, which a folder copied elsewhere cannot produce.

## Decision

### Archives are self-contained ZIPs with regenerated identities

`.macroDeckProfile`, `.macroDeckFolder` and `.macroDeckWidget` are built by one portable-archive
subsystem: a plaintext `manifest.json` plus either plaintext content and assets or an encrypted payload.
Imported profiles, folders, widgets, secrets and other machine-local identities receive fresh ids and
every reference is rewritten consistently.

**Secrets are excluded by default.** Including them requires a password and encrypts the *complete*
payload rather than leaving content selectively exposed. Password protection uses a derived key and
authenticated encryption; the manifest's encryption parameters are authenticated with the payload,
parameters are bounded and validated before expensive work, and extraction has entry and size limits to
resist hostile archives.

Folder archives reuse the container but preserve folder-specific invariants: the imported root is
detached from its source parent, does not replace the target profile's start folder, its widgets are not
made globally pinned, and grid dimensions are resolved so the layout stays valid.

Desktop path-based import endpoints reuse the same import services but are restricted to the trusted
loopback path, because remote clients must not gain a host filesystem oracle. Uploads are deliberately
*not* restricted: bytes the caller already holds reveal nothing about this machine's filesystem.

### A migration source produces the same portable content

A migration source reads a foreign installation and produces the `PortableContent` the archive importer
already consumes, so id minting, reference rewriting, persistence and notifications stay in one
implementation rather than being reimplemented per source.

**Configuration travels beside the portable model, not inside it.** A portable archive is a shareable
artefact; putting the exporter's hosts, client ids and secret references into it would leak them into
every shared profile. A migration plan therefore carries integration configuration, variables and icon
requests as sibling collections the archive format never sees. Icons go through the ordinary icon import
rather than the portable asset path, because a foreign application ships loose PNG and GIF where the
portable path expects processed WebP.

**An integration contributes its own translations** through the SDK's `IMigrationProvider`, discovered
by interface like every other capability. The capability is the *list*: one integration commonly reads
several applications, so it declares one migration per application, each naming its source. Unlike every
other registry this one does **not** filter by enabled state — an integration with no configuration yet
is disabled, and migrating is what gives it one.

The capability is published and served over the plugin protocol as the `migration` capability kind, so
an out-of-process plugin takes its own setup over exactly as a built-in does. Both translation members
are asynchronous, because a remote plugin answers over a connection and a synchronous contract could
only be served by blocking on it ([ADR 0062](0062-ui-realtime-transport.md)). A source name the host
does not recognise is dropped from a declaration rather than refused, so a plugin built against a later
SDK stays usable.

A foreign action nothing claims becomes a **placeholder action that carries the original type and
configuration and fails when run**. A skipped block would be reported as skipped, and a migrated deck
that silently does nothing is worse than one that says what it could not bring across. Every failure
crossing the plugin connection is answered as "no equivalent" rather than propagated, so an unreachable
plugin costs one placeholder instead of failing a migration that has already read hundreds of buttons.

**Decryption is offered, never assumed.** The key is read exactly as the source application reads it;
when that is impossible the preview says which case it hit and the user chooses between migrating
without the encrypted data and supplying a key by hand. A key that opens *some* files is accepted — a
folder routinely holds a file written under an older machine identity.

## Consequences

- Users can move or share a profile, folder subtree or widget selection without depending on
  source-machine ids, and plain archives are safe to share with respect to stored secrets.
- Secret-bearing archives remain subject to offline password guessing, so strong generated passwords are
  preferred.
- Archive formats are versioned compatibility contracts: a reader must reject a newer incompatible
  version rather than silently misread it. Import validates and plans placement before committing.
- Adding a migration source is a reader plus a translation. Reading is pure, which is what lets preview
  and import run the same code and what makes preview a real guarantee.
- The archive reader's structural validation is bypassed for a migration, so a source owns its own: grid
  bounds, folder parentage and placement are checked before anything is created.
- `migration` is a public capability, so its DTOs, operation names and source vocabulary are a
  compatibility commitment — sources are added, never renamed or renumbered.
- Migration is additive: running it twice yields a second copy of each profile, exactly as importing the
  same archive twice does. An integration that already has a configuration entry is left alone.
- Preview and import are separate requests, so an uploaded backup is sent twice. That is the price of
  keeping the server free of per-migration session state.
- Applying a migration is refused while the key ring is locked
  ([ADR 0047](0047-secrets-backups-and-restore.md)), because secrets cannot be stored in that state.

## References

- [Issue #35](https://github.com/Macro-Deck-App/Macro-Deck/issues/35),
  [Issue #365](https://github.com/Macro-Deck-App/Macro-Deck/issues/365)
- [ADR 0022](0022-icon-identity-and-widget-icons.md)
