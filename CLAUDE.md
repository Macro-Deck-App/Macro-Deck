# Claude Code guidance

Macro Deck 3: .NET host (owns state and business logic), an Angular desktop UI and a framework-free
web client (REST + JSON WebSocket), Tauri bootstrapper (owns the native window and host process).

## Workflow

Branch before working. Prefix `feature/`, `fix/`, `refactor/`, `chore/`, `docs/`, `ci/` plus a short
kebab-case description and the issue number when one exists.

## Public SDK and plugin compatibility

A plugin may stay compiled against an old SDK indefinitely. Every non-obsolete public plugin-facing
contract is a compatibility commitment: `sdk/`, `ui-model/`, `protocol/`, plugin HTTP/WebSocket
behaviour, manifest and package formats, analyzer diagnostic ids, conformance check ids. Source and
binary compatibility both count.

**If a fix needs to break one, stop and ask before implementing it.** Protocol breaks require a new
protocol major with the previous version still served.

When these surfaces change, run the compatibility, conformance, deprecation-lifecycle, protocol, and
package tests, and add a regression test from the old consumer's point of view.

See [`docs/`](docs/) and [ADR 0026](engineering/decisions/0026-plugin-protocol-and-sdk-boundary.md).

## Testing

Derive expectations from the requirement or contract, never from the current implementation. When a
test and the implementation disagree, go back to the requirement instead of editing either side.

Prefer a few meaningful scenarios to coverage. Do not test private methods, internal call sequences,
trivial accessors, or framework behaviour; mock real external boundaries only.

## Localization

**No user-facing hardcoded strings.** Anything a person can read in the app (labels, headings, buttons,
tooltips, aria-labels, placeholders, dialog and toast text, validation and error messages, empty states,
status text, integration action and parameter names, native menu and dialog text) comes from a
localization resource, and ships with a translation in **every** language the app already carries. English
is the default and the fallback; a new key missing a value in any other shipped language (German, Italian,
Czech, Polish, Spanish, French) is an incomplete change, not a follow-up.

Internal strings are exempt and stay as they are: `ILogger` messages, exception text that is only ever
diagnostic, protocol and enum values, error *codes*, identifiers, config keys, routes, storage keys, CSS
class names, icon names, and test ids.

New keys go in `macrodeck.app` (`host/src/MacroDeckHost.Localization/Localization/`), reached through the
generated `AppStrings` in C#, `ClientAppStrings` from `@macro-deck/runtime` in the web client, and
`AppStrings` / `'macrodeck.app:Key' | translate` in Angular. The web client compiles in only the slice of
the catalog it can paint - the whole thing is half a megabyte of strings a deck never shows - so a key it
needs must sit under one of the namespaces listed in `GeneratedTypeScriptDriftTests`, or that list grows
and the modules are regenerated. Only add to
`macrodeck` (`sdk/src/MacroDeck.Localization/Resources/`) when the string is genuinely reusable by
plugins: that catalog is published in the SDK, and its keys are a frozen, additive-only contract.

Reuse an existing key rather than minting a duplicate, keep one key per sentence with `{placeholders}`
for values instead of concatenating translated fragments, and use a `[plural]` family for anything that
counts.

Translate for meaning, not word by word: a translated label may be phrased differently from the English
one where that reads better. Each shipped language has its own established register, kept consistent
across the whole catalog:

- German, Italian, Spanish, French: informal address (`du`/`tu`/`tú`/`tu`), imperatives without a pronoun
  (`Wähle ein Ereignis`, not `Wählen Sie ein Ereignis`), lowercase mid-sentence for German.
- Czech, Polish: impersonal phrasing, meaning imperative verb forms for actions and impersonal statuses/errors
  rather than direct `ty` address, matching each language's own desktop-software convention.

Czech and Polish also need a plural-form adjustment the other languages don't: the localization compiler
only distinguishes `count == 1` from every other count (see
[the localization guide](docs/src/content/docs/features/localization.md#only-one-and-other)), which is
grammatically exact for German/Italian/Spanish/French but not for Czech/Polish's `few`/`many` forms. Phrase
a Czech or Polish `Other` form to avoid noun-count agreement (a count-agnostic label rather than a declined
noun) so it stays grammatical for every count.

After changing a resource, regenerate the checked-in TypeScript and Rust catalogs:

```bash
MACRODECK_UPDATE_GENERATED=1 dotnet test sdk/tests/MacroDeck.Localization.Tests.UnitTests
```

See [`docs/src/content/docs/features/localization.md`](docs/src/content/docs/features/localization.md).

## Code comments

Internal implementation code normally carries no comments and no XML documentation. Comment only for
a reason the code cannot express: a security or trust assumption, a compatibility or protocol
constraint, a platform workaround, an ordering or race requirement, deliberately surprising
behaviour, an external constraint.

Such a comment is a `//` line comment of **at most two lines**. No block comments and no JSDoc in
implementation code, in any language: no `/* */`, no `/** */`, no `<!-- -->`. If two lines cannot
carry the reason, the reason is too large for a comment and belongs in `engineering/` or an ADR that
the code names. Two short comments stacked to dodge the limit is the same thing as one long one.

Do not use em dashes or backticks in comments. A colon, a full stop or a plain hyphen carries the
same break, and a bare identifier reads the same as a quoted one.

Public SDK/plugin members are the one exception, because their XML docs are a published contract:
they get concise `///` docs for non-obvious contract semantics, lifecycle, side effects, and failure
behaviour, never a restatement of the signature.

## Documentation

`docs/` is the published Astro/Starlight developer site: public plugin, SDK, protocol, and
compatibility documentation only, no standalone internal files. `engineering/` is internal
architecture, workflow, and ADRs. ADRs use
[`0000-template.md`](engineering/decisions/0000-template.md) and exist only for project-wide
decisions that are costly to reverse.

Link to code, config, or schemas instead of restating them: if a document would have to change
whenever an implementation detail or CI job name changes, reference the source instead.

Update documentation only for a change to a public developer contract, the security/trust model, a
contributor workflow, a release operation, or an architecture decision.

Package-local `README.md` files are NuGet `PackageReadmeFile` assets. Do not remove, rename, merge,
or move them during documentation cleanup unless you update package metadata and verify the
resulting `.nupkg` readme.

## Common commands

```bash
dotnet build MacroDeck.slnx -c Release -warnaserror
dotnet test MacroDeck.slnx -c Release
```

`ui/angular/`: `npm run build:prod`, `npm test`. desktop-ui is the only Angular project; it consumes
`ui/runtime/` from its build output, which the repo npm scripts build first.

`ui/web-client/`: `npm run build`, `npm test`. The public client is framework-free and built rather
than served; it and desktop-ui both consume `ui/runtime/`, which builds first.

`ui/bootstrapper/`: `cargo fmt --check`, `cargo clippy --all-targets -- -D warnings`, `cargo test`.

More: [`engineering/README.md`](engineering/README.md), [`docs/README.md`](docs/README.md).
