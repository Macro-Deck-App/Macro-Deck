# ADR 0111: The host database is migrated by EF Core

Status: Accepted

## Context

The host migrated its SQLite database with Evolve and SQL scripts named `V1_x_0_0__Name.sql`. Evolve
splits those names with a culture-sensitive `IndexOf`, and the Thai collation ignores `_`. On a
system with a Thai regional format every start failed before the host listened, and a reinstall could
not help ([#1405](https://github.com/Macro-Deck-App/Macro-Deck/issues/1405)). Restoring a backup ran
the same code.

The host already reads and writes through an EF Core model, so the scripts and the model described the
same schema twice.

## Decision

The schema is defined by the EF Core model and changed only through EF Core migrations generated with
`dotnet ef migrations add`. The first migration, `Initial`, is the schema the last Evolve script
produced; the model configures every key, index name, default and collation those scripts declared,
and a test compares both schemas column by column. Evolve is removed.

Migration ids are fixed-width timestamps. EF Core sorts them with the default string comparer, which
is culture-sensitive, but ids of equal length that start with digits sort the same in every culture.

Until the legacy path is removed, a database that carries an Evolve history and no EF Core history is
brought up to the last Evolve script by a small runner over the embedded former scripts, which reads
versions ordinally and applies Evolve's own rules (only successful migration rows count, unknown older
rows are ignored, a newer or gapped history is refused). In the same transaction it records `Initial`
as applied. The Evolve history table stays untouched. The same path migrates the database inside a
backup archive before it is restored.

The legacy path, the embedded scripts and the Evolve fallbacks in backup and restore are removed no
later than the 3.1.0 release ([#1406](https://github.com/Macro-Deck-App/Macro-Deck/issues/1406)).
After that, a database or archive that never passed through a 3.0.x host is refused.

The data root no longer has a migrations directory: the scripts that remain are embedded resources.
This supersedes the sentence on `DatabaseMigrationsDirectory` in
[ADR 0002](0002-platform-data-directories-and-build-channels.md).

## Consequences

- No culture-dependent parsing is left on the startup or restore path.
- A schema change is an entity or configuration change plus a generated migration; a test fails when
  the model has changes no migration covers. Generated migrations are committed as the tool wrote
  them and are excluded from analyzers and code cleanup.
- Before every migration the host deletes a stale row in EF Core's SQLite migration lock table, since
  that lock never expires and a host killed mid-migration would otherwise block every later start.
  Restore migrates a private copy, but the live single-instance check is a best-effort probe: two hosts
  started at the same moment can both migrate, and the loser fails its start without losing data.
- Downgrading is unsupported. A database created by EF Core has no Evolve history, and one lifted from
  an older Evolve version keeps that older history, so a pre-switch build fails on both. Only a
  database that was at the last Evolve script before the switch still starts on an older build, until
  the next schema change. An archive of an EF Core database restored on a pre-switch build is refused
  as an unsupported version.
- Backup manifests record the newest EF Core migration id as the schema version instead of an Evolve
  version; the field is informational.
