---
title: Theming
description: Theme roles versus literal colours, and how text carries localization and typeface across a Macro Deck UI tree.
---

## Colours

Anything theme-derived is a **role** - `primary`, `secondary`, `muted` - which each renderer resolves
against its own live theme, so a theme change repaints without your view being involved. Send a literal
`#rrggbb` only where the colour is data rather than theme: a value's colour, or one a person picked, must
not change when the reader switches themes. A renderer rejects any other spelling.

That is what `UiTextRun.Color`, `UiStack.Background`, `UiButton.Background`, `UiButton.BorderColor` and
`UiSlider.LevelColor` are for - each overrides the theme where a user chose the colour. Omitting one is
meaningful in its own right: a text falls back to its role, a stack paints nothing, a button ring takes
whatever colour its style supplies, and a slider - or a button's face - paints the reader's **own** accent
colour. Resolving your accent to hex and sending that instead would freeze it against the reader's theme.

| property | takes a role | takes a literal `#rrggbb` | omitted means |
| --- | --- | --- | --- |
| `UiTextRun.Color` | yes | yes, for data the user shouldn't see re-themed | falls back to the run's theme role |
| `UiStack.Background` | no | yes | paints nothing |
| `UiButton.Background` | no | yes | the reader's own accent colour |
| `UiButton.BorderColor` | no | yes | the border style supplies its own colour |
| `UiSlider.LevelColor` | no | yes | the reader's own accent colour |

## Text

Every user-visible string should be a localization reference rather than a resolved string. `UiText`
accepts a `LocalizedString` directly, and each client resolves it in its own language - which is what lets
one shared session serve clients in different languages, and what makes a language change a client-side
re-render rather than a new tree.

`Wrap` lets a run break across lines; omitting it keeps the run on one line and ellipsized, which is what
a reader that does not implement the key does anyway. `FontFace` names a typeface from Macro Deck's own
font catalogue - an identifier rather than a resource handle, because a face is host-owned and routinely
far larger than one resource may be. A reader holds the run back until the face is usable rather than
drawing it in a fallback and swapping, and reveals it in its own default face if the face never arrives: a
run that never appears is worse than one in the wrong font.

See [Localization](/features/localization/) for the resource catalogs `LocalizedString` draws from, and
[Resources](/ui/reference/resources/) for how a `FontFace` identifier differs from a `UiResource`
handle.
