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

## Built-in integrations

Every built-in integration is built so that it could be split out into an out-of-process plugin
without much effort. It is a plugin that happens to run in-process:

- It reaches the host only through the SDK: `IIntegrationContext`, the provider interfaces and the
  `MacroDeck.Ui` DSL. No host services, no types from the host projects, no host-injected setter or
  consumer interfaces, no other code that finds and casts the integration instance.
- Everything it owns lives in its own folder in `MacroDeckHost.Integrations`: actions, config flow,
  state, widget types, widget and dialog UI (served through `IUiProvider` as a plugin serves it) and
  assets. None of it goes into `Application`, `Infrastructure`, `Widgets` or the host project.
  `MacroDeckHost.Widgets` holds only shared base widgets: the host's own widget types and widgets
  for an SDK capability any provider, a plugin included, can feed.
- If it needs something the SDK does not offer, the SDK gains it as a public, documented addition that
  works out of process too, protocol included, so plugins can use it as well. That is public-contract
  work under the rules above, not a reason for a host-only shortcut.

Code that predates this rule (integration hubs and sinks in `Application`, integration widgets in
`MacroDeckHost.Widgets`, host-side adapters for single integrations) is known debt: never extend it
or copy its pattern. `IntegrationBoundaryTests` enforce this; their baseline lists the debt and may
only shrink, so never add a line to it. System integrations (`ISystemIntegration`) are host features
and exempt. See
[ADR 0107](engineering/decisions/0107-built-in-integrations-are-built-as-extractable-plugins.md).

## Dependencies

Never install or add a third-party package just because it looks useful or because a name was
suggested or generated during implementation: hallucinated and typosquatted names are an active
supply-chain attack on coding agents. Restoring what the existing manifests and lockfiles declare is
fine. A new dependency in any ecosystem (npm, NuGet, Cargo, Swift Package Manager, Gradle/Maven, Python,
brew, apt and the like) needs a reason the existing dependencies and the platform cannot cover, a check
that the exact name exists on the official registry and is the legitimate package, and the user's
explicit approval before any install or manifest or lockfile change. When asking, name the package,
its purpose, its registry and why the existing dependencies are not enough.

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
Czech, Polish, Spanish, French, Simplified Chinese, Traditional Chinese, Japanese, Korean, Russian, Ukrainian,
Brazilian Portuguese, Arabic, Hindi, Turkish, Dutch, Indonesian) is an incomplete change, not a follow-up.

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
- Simplified Chinese (`Strings.zh.resx`): no pronoun where it can be avoided, `你` where it cannot,
  mainland desktop-software terminology, and `One` and `Other` forms with the same wording.
- Traditional Chinese (`Strings.zh-TW.resx`): Taiwan desktop-software terminology (`檔案`, `設定`,
  `資料夾`), no pronoun where it can be avoided, `您` where it cannot, same wording for `One` and `Other`.
- Japanese, Korean: polite sentences (`です`/`ます`, `하세요`/`합니다`), short noun phrases for labels and
  buttons, same wording for `One` and `Other`.
- Russian, Ukrainian: polite plural imperatives (`Выберите`, `Виберіть`), impersonal statuses and errors.
- Brazilian Portuguese: imperatives without a pronoun (`Selecione`), `você` only when unavoidable.
- Arabic: Modern Standard Arabic, imperatives for actions; Latin names, identifiers and placeholders stay
  as they are.
- Hindi: Devanagari with polite `आप` forms (`चुनें`).
- Turkish: polite imperatives (`Seçin`). Dutch: informal `je`, imperatives without a pronoun (`Kies`).
- Indonesian: no pronoun where it can be avoided, `Anda` where it cannot, same wording for `One` and
  `Other`.

Czech, Polish, Russian, Ukrainian, Arabic, Brazilian Portuguese and Hindi also need a plural-form
adjustment: the localization compiler only distinguishes `count == 1` from every other count (see
[the localization guide](docs/src/content/docs/features/localization.md#only-one-and-other)), which is
grammatically exact for German/Italian/Spanish/French/Dutch/Turkish but not for languages with
`few`/`many` forms or with `0` counted as singular. Phrase their `Other` form to avoid noun-count agreement
(a count-agnostic label rather than a declined noun) so it stays grammatical for every count.

After changing a resource, regenerate the checked-in TypeScript and Rust catalogs:

```bash
MACRODECK_UPDATE_GENERATED=1 dotnet test sdk/tests/MacroDeck.Localization.Tests.UnitTests
```

See [`docs/src/content/docs/features/localization.md`](docs/src/content/docs/features/localization.md).

## Icons

App icons come from [Lucide](https://lucide.dev), which includes the Feather set. When Lucide has no icon
for the concept, draw one in the same style: 24x24 viewBox, no fill, stroke width 2, round caps and joins.
Mask icons in `ui/runtime/styles/icons/` use `stroke="#000"`; icons shown as images set the colour they need.

The only exceptions are logos of third-party products (take them from Dashboard Icons or Simple Icons,
otherwise the vendor's own logo), the Macro Deck logo and branding, the weather widget's icons and the
music player's animated playing badge. Every icon file is
either attributed in `third-party/attributions.yml` or listed as first-party in `third-party/config.yml`; the
license tool fails otherwise (see [third-party notices](engineering/development/third-party-notices.md)).

Published `ui.icon` names keep their meaning: an icon may be redrawn in this style, never replaced by a
different concept ([ADR 0084](engineering/decisions/0084-built-in-icon-names-are-a-versioned-public-vocabulary.md)).

## Cursors

The UI follows native desktop conventions: the arrow on everything that looks like a control (buttons,
icon buttons, checkboxes, switches, tabs, menu and list rows, chips, sliders), the hand only on links,
clickable cards and tiles, and clickable content that does not look like a control. Drag handles use
`grab` and `grabbing`, text inputs `text`, disabled controls keep `not-allowed`.

A `button`, `input` or `select` already has the arrow, so declare nothing. On any other element
(`div`, `span`, `a`, `label`, `summary`) write `cursor: default`, otherwise it shows the I-beam over
text or the anchor's hand. A deck key tile or press-claiming node keeps the hand; a value control
(slider, dial, toggle, segmented) and a button inside dialog content do not.

`cursors.test.mjs` in `ui/angular` fails on any `cursor: pointer` that is not on its allowlist; adding
a link or card means adding its line there.

## C# code style

[`MacroDeck.slnx.DotSettings`](MacroDeck.slnx.DotSettings) is the C# style, applied in Rider by
"Reformat & Apply Syntax Style". Nothing reformats C# after it is written, so write it the way that
cleanup would leave it:

- Methods, constructors, operators, local functions and default interface methods get a block body,
  one-liners included (`ToString()`, `throw new NotSupportedException();`, `return Task.CompletedTask;`).
  Properties, indexers and lambdas stay expression-bodied; a long one breaks before `=>`.
- `new()` only where the declaration names the type (field, property, explicitly typed local).
  Everywhere else write the type: `return new Foo { ... };`, `Use(new Foo(...))`.
- Positional arguments. Name an argument only where the call cannot compile without it.
- No trailing comma in object, collection, dictionary or anonymous-object initializers, collection
  expressions or enums.
- No redundant parentheses: `a | b << 8`, `State is A or B && ready`.
- No unused `using` directives; keep them sorted.
- Wrap at 120 columns, a tab counting as 4:
  - Binary operators (`&&`, `||`, `??`, `+`) and the `?` and `:` of a conditional start the
    continuation line, never end the previous one.
  - A declaration whose parameters do not fit breaks right after `(` with one parameter per line.
  - A call that does not fit keeps its first argument after `(` and puts each further argument on
    its own line.
  - A method chain that does not fit puts each `.Call()` on its own line.
  - An object or collection initializer that does not fit puts each entry on its own line.
  - A declaration whose initializer does not fit breaks before `=`.
- Naming: private static fields, `static readonly` included, are `_camelCase`; constants are
  `PascalCase` at every access level, interop constants too (`WmQuit`, not `WM_QUIT`); acronyms
  are words (`CfString`, `ReadNsString`, `Msg`); no trailing underscores. Only `extern` and
  `DllImport`/`LibraryImport` entry points keep their native names.
- NUnit: `await Assert.MultipleAsync(async () => ...)`, never `Assert.Multiple` with an async lambda.

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

`docs/` is the published Astro/Starlight site: a user guide under `guide/` for people using Macro
Deck, and public plugin, SDK, protocol, and compatibility documentation for developers. No
standalone internal files. `engineering/` is internal
architecture, workflow, and ADRs. ADRs use
[`0000-template.md`](engineering/decisions/0000-template.md) and exist only for project-wide
decisions that are costly to reverse.

Link to code, config, or schemas instead of restating them: if a document would have to change
whenever an implementation detail or CI job name changes, reference the source instead.

Update developer documentation only for a change to a public developer contract, the security/trust
model, a contributor workflow, a release operation, or an architecture decision.

Keep the user guide (`docs/src/content/docs/guide/`) current in the same change: when you change
something a user sees or does (installation and packages, supported platforms and devices, settings
and their labels, setup steps, limitations, troubleshooting symptoms), update the affected guide pages
and, if the UI changed visibly, their screenshots under `docs/src/assets/guide/`. Take screenshots
from a disposable instance as described in
[`docs/README.md`](docs/README.md#user-guide-screenshots).

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
