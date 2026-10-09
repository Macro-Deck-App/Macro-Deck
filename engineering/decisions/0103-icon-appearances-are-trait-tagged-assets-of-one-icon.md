# ADR 0103: Icon appearances are trait-tagged assets of one icon

Status: Accepted

## Context

An icon was exactly one image. Light and dark versions, or an animated icon and a still version of it,
had to be imported as unrelated icons, and nothing switched between them. The deck background is chosen
per profile and folder, the app theme is one host-wide setting that `system` resolves per device, and a
deck can be shown on several devices at once, each with its own theme and reduced-motion preference. So
the host cannot pick one image per icon for everyone.

Icon identity is already settled by [ADR 0022](0022-icon-identity-and-widget-icons.md): a widget stores
`{type, reference}`, and for the icon pack catalog the reference is the icon's GUID. Plugins, device
surfaces (`DeviceSurfaceAppearance.IconId`) and action parameters hold that GUID as a public value. The
`.macroDeckIconPack` `pack.json` has no format version, older hosts ignore unknown JSON properties, and a
new enum value makes an older host reject the whole pack ([ADR 0097](0097-plugin-bundled-icon-packs.md)).

## Decision

**An icon keeps its GUID and its own image, which is its default appearance.** Additional appearances
belong to that icon. Every existing reference keeps meaning "this icon", and an icon without appearances
is stored, served and versioned exactly as before.

**An appearance is a hidden asset tagged with traits.** It is stored and processed like an icon (its own
id, master and size renditions) but is never listed, counted, deduplicated against or offered as a
standalone icon, and it is removed with its icon. Its traits are an open string vocabulary, `key=value`
pairs such as `colorScheme=dark` or `motion=static`, with at most one appearance per trait set. Today
the keys `colorScheme` (`light`, `dark`) and `motion` (`static`, `animated`) are understood. A new kind
of appearance is a new trait key, not a schema change: unknown traits are kept and round-trip, and they
never match.

**The context comes from the viewer, and the host selects.** A client sends the context it renders in
(`colorScheme` from its resolved theme and `motion=static` under reduced motion) as query parameters on
the image URL. A device surface derives `motion` from its layout's declared visual capabilities. The host
picks the ready appearance whose traits all match the context, preferring more matching traits, with
`motion` deciding a tie under reduced motion and `colorScheme` otherwise, and falls back to the default.
Selection only ever switches between images the user or creator provided. Nothing is frozen to a first
frame because of reduced motion.

**Only icons with appearances vary by context.** Their resource ids carry a `.a` suffix, and only those
URLs get context parameters, so a plain icon keeps its URL, `v` and ETag, and toggling the theme refetches
only icons that can change. The version of an icon with appearances folds in every appearance's hash and
traits, so replacing one appearance invalidates every cached URL of the icon.

**A user can name appearances of their own.** A custom appearance carries the single trait `variant=<token>`,
for example `variant=outlined`, and is not matched by any viewer context, so it is only ever shown when a use of
the icon pins it. The user types a name and the UI reduces it to the token: diacritics folded, words joined in
camel case, ASCII letters and digits only, at most 32 characters. The label shown is the token spelled out
again ("Duo tone"), because the name itself is not stored. Names in other scripts are therefore refused for
now; storing a display label is a persisted-format change and is left to a follow-up. The pin dropdown, the Icon
Library and the Set Icon Appearance action list the appearances that exist, so a custom one is selectable
wherever a built-in one is. The limit of eight appearances per icon covers both kinds.

**A use of an icon can pin an appearance.** A widget icon reference may carry `appearance` (a trait set,
or `default`). Absent means automatic. A pin that no longer matches an appearance falls back to automatic.

**Formats change additively.** In `pack.json` and in portable archives, appearances are nested under
their icon, and their masters are ordinary `icons/<id>/master.webp` entries listed in `files`. No format
version is bumped and no enum value is added. An older host imports the pack and shows the default
images.

**Merging an icon into another rewrites its references.** Folders, automations and scripts that used the
merged icon now use the target with automatic selection.

## Consequences

- One selection rule serves the deck, the desktop preview, the Icon Library and devices.
- Clients that never send context parameters, such as an outdated companion app, keep showing the default
  appearance.
- Trait metadata is not covered by the icon pack signature, just like icon names: a re-pack can relabel
  which signed image is "dark". The image bytes themselves stay covered through `files`.
- After a downgrade, an older host rewrites its internal `pack.json` without appearances. Their files stay
  on disk, orphaned.
- Ids held outside folders, automations and scripts, such as a plugin's stored icon id or a plugin icon
  handle it already received, are not rewritten by a merge. They keep showing the merged image directly.
- Appearance files count against the archive entry and size limits like any other icon file.

## References

- [ADR 0022](0022-icon-identity-and-widget-icons.md), [ADR 0097](0097-plugin-bundled-icon-packs.md)
- Related request for pack-level styles: [#1197](https://github.com/Macro-Deck-App/Macro-Deck/issues/1197)
