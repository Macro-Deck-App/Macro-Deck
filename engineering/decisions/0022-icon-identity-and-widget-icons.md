# ADR 0022: Icon identity is a locally computed hash, and a widget icon is a typed provider reference

Status: Accepted

## Context

The same icon reaches Macro Deck through many paths — an upload, a file dropped on a widget, an icon
extracted from an executable, an icon pack, a portable archive — and every one minted a fresh id and
stored another copy, because nothing looked at the content first
([#284](https://github.com/Macro-Deck-App/Macro-Deck/issues/284)).

One deduplication did exist, and its second flaw is why this needed a decision rather than a patch: it
compared a checksum that came **out of the archive**. A portable archive bundles processed WebP
renditions and never the original source file, so a hash of those originals describes bytes the archive
does not contain and nothing can check it. A hand-edited archive could declare the source hash of an
icon the importing user already had, and the import would silently point the widget at that icon
instead. The generic `Checksum` name made this easy to get wrong: one property held what was sometimes
a locally computed hash and sometimes an unverified claim.

Separately, a widget's icon was a bare `iconId` string, and every consumer independently re-derived
whether it was an icon-pack GUID and treated a parse failure as "no icon". Nothing could supply a
widget's icon except the icon pack catalog, which
[#425](https://github.com/Macro-Deck-App/Macro-Deck/issues/425) needed: an integration must be able to
own the image a widget currently renders — album artwork, an avatar — without those bytes being copied
into a user's icon pack.

## Decision

### A hash is a lookup key only if this host computed it over bytes it actually read

Three properties replace `Checksum`, and two distinct `readonly record struct` types keep the first two
from being passed where the other is expected:

| Property | Bytes | Key |
|---|---|---|
| `SourceContentHash` | the original imported file, hashed during staging | yes |
| `MasterContentHash` | the stored `master.webp` | yes |
| `DeclaredSourceContentHash` | whatever an archive claimed | never |

Values are written as `sha256:<hex>` so the algorithm travels with the hash, and `ContentHash` is the
only place one is produced or parsed.

Hashing happens at staging, in the pass that was writing the bytes anyway, so identity exists before an
icon entity is registered. The index lives in `IconPackCache`, which already owns every icon and
serialises its mutations; only `Ready` icons with a master still on disk are candidates, so a caller
never receives an id it cannot immediately reference. Lookups are deliberately lock-free — one landing
inside a rebuild can miss, and the cost of a miss is a duplicate icon, never a wrong one.

- **Reference-oriented imports reuse across the whole catalog**, including read-only packs, because
  reuse only ever reads.
- **Imports into a chosen pack deduplicate within that pack only.** An identical icon in another pack
  never stands in for a requested item: a pack keeps its documented membership, names and ordering.
  There is no shared content-addressed blob store. What was collapsed is reported on the batch, so an
  import producing fewer icons than files says why.
- **Portable archive imports key on a hash of the bundled bytes**, so a match proves byte equality with
  what the archive carries. A declared hash is verified against those bytes as an integrity check and,
  on mismatch, the icon is skipped rather than written.
- **No stored `Checksum` is trusted.** Existing values migrate to `DeclaredSourceContentHash`; those
  icons still deduplicate through their master hash, which a background pass computes from the files.

### A widget icon is `{type, reference}`, and `reference` is opaque

The icon pack catalog is the first provider, and its reference happens to be a GUID; nothing that
resolves a reference may assume that. Resolution goes through one `IWidgetIconSource` registry, so an
unregistered type renders as no icon exactly as an unparseable id always did. Legacy `iconId` is read
forever and migrated on load.

**An icon provider owns the currently rendered icon, not one icon per state.** Album artwork is
independent of whether a button reads "playing" or "paused". It overrides at render time and **never
writes stored appearance**: the configured per-state icons stay where they are, which is why a profile
exported while a provider is active still carries them and still renders them on a machine where the
providing plugin is not installed. This is the deliberate divergence from
[ADR 0056](0056-widget-state-is-addressed-by-stable-state-id.md), which stashes a button's manual states
because a state provider replaces them.

**Three provider answers are deliberately distinguishable.** `null` means "cannot answer" — unreachable,
timed out, threw, integration gone — and falls back to the widget's configured icon. An explicit
`NoIcon` means the provider is working and showing nothing. Anything else is an image. Collapsing the
first two would make a disconnected integration and a deliberately blank button indistinguishable.

**Unavailability falls back rather than holding**, the opposite of a state provider: an icon has a
meaningful default, and holding a stale album cover for a track that stopped playing is worse than
showing the icon the user configured.

Identity is polled cheaply and bytes are fetched only when it moves; a coarse invalidate push is keyed
by *action*, because a configured instance has no wire identity.

## Consequences

- An icon dropped twice, an application icon extracted twice, and a profile exported and re-imported on
  the same machine all resolve to one icon instead of three copies.
- A portable archive cannot make an import reuse the wrong icon, whatever it declares.
- A pre-existing icon cannot be matched by a freshly dropped *source* file until it is imported once
  more. That is the price of not trusting a checksum whose origin is unknown, and it decays over time.
- Two different source files that encode to byte-identical masters are treated as one icon on archive
  import. They render identically at every size, but it is a merge, which is why master-hash matching is
  used only where the source bytes genuinely are not available.
- Provider bytes cross the plugin wire on the chunked asset channel, never in a capability reply, which
  is capped well below artwork size. They use their own asset kind rather than the one that signals a
  plugin's *own* catalog icon changed, which would otherwise make a plugin's icon flicker per track.
- **A provider-declared media type is not trusted.** Bytes are registered only for
  `image/png|jpeg|webp|gif`; a reference naming a URL is never fetched, because that would hand any
  installed plugin a request-forgery surface on the host.
- Provider images register under a host-generated, widget-scoped resource name, never the provider's raw
  reference, which would leak an entry per track and reject any reference containing `:` or `/`.
- Set Icon fails while a provider is authoritative. The guard is structural — an assignment exists —
  rather than resolving the whole chain, so a temporarily disabled integration does not silently revoke
  ownership the user assigned.

## Alternatives considered

- **Trust an archive's declared source hash once its master hash verifies.** Verifying the master proves
  the archive is internally consistent, not that its source hash describes those pixels. The source bytes
  are never in the archive, so the claim is unfalsifiable by construction.
- **Deduplicate only the generated WebP output.** Loses the identity of the input and cannot skip work
  early.
- **A content-addressed blob store shared across packs.** The only way to have one asset behind several
  catalog entries, but it means refcounting and rework of export, restore and deletion. Deduplicating
  within a pack removes the duplicates users actually create at a fraction of the risk.
- **Perceptual hashing.** Would merge intentionally distinct assets and quality variants.

## References

- [Issue #284](https://github.com/Macro-Deck-App/Macro-Deck/issues/284),
  [Issue #425](https://github.com/Macro-Deck-App/Macro-Deck/issues/425)
