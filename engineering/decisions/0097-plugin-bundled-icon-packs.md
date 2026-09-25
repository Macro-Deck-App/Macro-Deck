# ADR 0097: Plugin-bundled icon packs are owned by declaration and named by key and icon name

Status: Proposed

## Context

A plugin wants to ship the icons it needs and use them on buttons, in its UI views and as defaults in its
configuration forms. A locally made icon pack has no stable identity: import regenerates the
pack and icon GUIDs from its `pack.json`, and only Store packs carry a lasting `SourceId`. A plugin therefore
has nothing it can name from code. Buttons, widget data, automations and every client already store and draw
icon-pack references by icon GUID ([ADR 0022](0022-icon-identity-and-widget-icons.md)), and any plugin-facing
change is a compatibility commitment ([ADR 0026](0026-plugin-protocol-and-sdk-boundary.md)).

## Decision

**Identity.** A bundled pack is identified by the owning plugin's id plus a key unique within that plugin. The
host stores it as an ordinary icon pack with `SourceType` `Plugin` and `SourceId` `<plugin-id>/<key>`. Icons
inside it are addressed by name, unique case-insensitively within the pack. Nothing global and no GUID from the
archive is part of the identity.

**Boundary translation.** A plugin names its own icon as `plugin-icon` with reference `<key>/<name>`. The host
resolves it at the plugin boundary, the only place that knows the calling plugin, against that plugin's own
packs, and replaces it with the `icon-pack` GUID reference: in icon-provider snapshots, and in UI trees and
patches before validation (only objects with exactly `type` and `reference`; payloads without the marker are
relayed byte for byte). An unresolved reference becomes no icon. Nothing past the boundary carries the unscoped
form, so a plugin can never reach another plugin's pack and every downstream consumer stays unchanged. A UI
view gets an icon as a resource handle `app.macro-deck.plugin-icon.<icon-guid>`, served from the icon pack
store, never from the in-memory resource store, so it survives restarts and costs no quota.

**Ownership by declaration.** A stamped pack is owned, read-only and not deletable only while its key is
declared: by the installed active version's signed manifest (a path the signed `files` list does not cover is
ignored), or by a connected self-registered development session. A pack whose key is no longer declared stays
stamped but is an ordinary user pack; a later declaration adopts it again by `SourceId`, keeps its icon ids by
name and overwrites edits made meanwhile.

**One sync.** A single per-plugin operation derives the declared set from current state and brings the stamped
packs in line: add, replace in place by name (an icon whose master hash is unchanged is not touched), and remove
a pack or icon that is no longer declared unless a widget, automation, script or folder view still references
it, in which case it is kept, the user is notified, and the pack is the user's from then on until a
declaration takes it back. It runs after install, activate and uninstall (never
under the plugin install gate), before each launch, and for a development session on `icon-packs/sync-bundled`
and when that session ends. It waits for the icon and cache readiness with a bound that is spent once per host
lifetime. `SourceRevision`, the archive's signed digest or uploaded content hash, lets an unchanged pack be
skipped; any user change clears it.

**Deduplication.** ADR 0022 dedupes by host-computed content hash within a target pack. For a plugin pack the
names are the addresses, so two names with identical bytes stay two icons. What dedupes is the update: an
incoming icon whose master hash equals the stored one is left untouched.

**Export.** An exported plugin pack is written as a `User` pack without `SourceId` or `SourceRevision`, so every
existing host and Store ingestion can read it.

## Consequences

- An older host ignores `bundledIconPacks` and installs the plugin without its packs.
- A pack a button still uses outlives its plugin as an editable pack; reinstalling the plugin takes it back
  and discards the user's edits to it.
- Development packs of a plugin that is never installed remain as ordinary packs after the session.
- Values the plugin reads back from forms and views arrive as `icon-pack` references, not as `plugin-icon`.
- `pack.json` files written with `SourceType` `Plugin` are unreadable by a host downgraded below this change,
  like any new enum value.

## References

- Issue #1002
