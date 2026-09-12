---
title: Localization
description: Adding Localization/*.resx to a plugin, named placeholders, the generated typed API, the fallback chain, and the MDLOC diagnostics.
---

Macro Deck resolves user-facing text in the reader's own language rather than baking one language into
whatever a plugin produces. A plugin author never calls into a resolver directly: you add `.resx`
resources, `MacroDeck.Plugin.Analyzers`' source generator turns them into a typed API that returns a
deferred reference, and the host resolves that reference for whichever client is actually rendering it.

This is what lets one shared UI session be read in two languages at once, and what lets a language
change take effect without rebuilding your integration's UI.

## Add resources

Add `Localization/Strings.resx` to your project - the folder name and the `Strings` base name both
matter, since the analyzer package globs `Localization/**/*.resx` into `AdditionalFiles` and the base
name becomes the generated class name.

```xml
<data name="Connect" xml:space="preserve">
  <value>Connect</value>
</data>
<data name="ConnectedAs" xml:space="preserve">
  <value>Connected as {userName}</value>
</data>
```

`Strings.resx`, with no culture suffix, is the **default-language file**: the one every translation is
checked against, and the last resource the fallback chain tries before giving up. It is always required,
even if you intend to ship only one language.

### Culture-suffixed siblings

Add one file per translation, named `Strings.<culture>.resx` next to it:

```
Localization/
  Strings.resx          # default language
  Strings.de.resx        # German
  Strings.zh-Hant-TW.resx # Traditional Chinese, Taiwan
```

The suffix must be a well-formed BCP-47 name - `de`, `de-DE`, `zh-Hans` or `zh-Hant-TW` are all valid.
It is checked on its *shape*, not by asking .NET whether the culture exists: under ICU, .NET happily
manufactures a `CultureInfo` for almost anything that merely looks plausible, which would make a typo in
a culture name silently unreachable rather than a build error. A malformed suffix is
[MDLOC005](#mdloc005).

`.resx` is the canonical format because it is the one format [JetBrains Rider's Localization
Manager](#editing-with-riders-localization-manager) understands - see
[ADR 0057](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0057-localization-is-a-deferred-reader-resolved-reference.md)
for the alternatives that lost to it.

## Named placeholders

A placeholder is a bare name in braces, substituted by position of the name, not by argument order:

```
Connected as {userName}
```

The generator collects placeholders from the **default-language template only** - a translation may use
a subset or a superset by mistake, and that mismatch is exactly what
[MDLOC002](#mdloc002) catches. Two literal braces (`{{`, `}}`) escape to one.

A placeholder's C# and TypeScript type is inferred from where it is used: with nothing else declared, it
is `string`. Where a narrower type is needed - a limit that should reject `"abc"` at the call site rather
than at render time - declare it as a bracketed prefix on the entry's `<comment>`:

```xml
<data name="Validation.TooLong" xml:space="preserve">
  <value>{field} exceeds {limit} characters</value>
  <comment>[limit:int] Character limit, not byte length.</comment>
</data>
```

Only the bracketed prefix is a declaration; everything after it stays the translator note Rider shows
next to the value. Supported types are `string`, `int`, `long`, `double` and `bool` - deliberately few,
so the C# and TypeScript formatters can agree on one unambiguous text form for every value (no thousands
separators, an invariant decimal point, lowercase booleans) without either side reaching for
culture-dependent formatting. Declaring a type for a placeholder the template does not use, or declaring
an unsupported type name, is [MDLOC004](#mdloc004).

## The generated API

Building the project turns `Strings.resx` into a `public static partial class Strings` alongside your
own code, one method per key returning `LocalizedString`:

```csharp
public static LocalizedString Connect();
public static LocalizedString ConnectedAs(string userName);
```

A dotted key becomes a nested class - `Configuration.Title` in the resource becomes
`Strings.Configuration.Title()` in code. Call it wherever an SDK member accepts `LocalizedText` -
literal strings and `LocalizedString` both convert implicitly, so `Label = "Client ID"` and
`Label = MyStrings.ClientId()` both compile:

```csharp
new UiStringInput { Key = "userName", Label = Strings.ConnectedAs(userName: connection.User) }
```

**The return type is deliberate.** `Strings.Connect()` returns `LocalizedString`, not `string` - a
reference to be resolved later, by the host, in whichever culture the reader is on. Assigning it to a
`string` is a compile error. If it returned already-resolved text, a language change could only take
effect by rebuilding your UI.

### Reuse `MacroDeckStrings` instead of duplicating common strings

Macro Deck ships its own reusable catalog - `Common.Save`, `Common.Cancel`, `Validation.Required` and
similar - generated the same way, from the SDK's own `Localization/Strings.resx`, into
`MacroDeckStrings`:

```csharp
new UiHeading { Key = "confirm", Text = MacroDeckStrings.Common.Save() }
```

Use it for anything generic rather than declaring your own copy: a duplicated `Save` key in your own
catalog is one more string every translator has to keep in sync with Macro Deck's, for text the reader
already sees translated consistently everywhere else in the app. `MacroDeckStrings` entries live in
scope `macrodeck`, not `plugin:<your-id>`, so they resolve against Macro Deck's own catalog wherever
they are used.

### Scope and class name

The generated class's scope defaults to `plugin:<id>`, reading `id` out of your project's
`manifest.json` - the same identity [MDP1001](/reference/analyzers/#mdp1001) already validates. The
class name defaults to the resource set's base name (`Strings`). Both are overridable, project-wide,
through MSBuild properties if you need to:

```xml
<PropertyGroup>
  <MacroDeckLocalizationScope>plugin:com.example.spotify</MacroDeckLocalizationScope>
  <MacroDeckLocalizationClassName>SpotifyStrings</MacroDeckLocalizationClassName>
</PropertyGroup>
```

A plugin owns exactly `plugin:<its-own-id>` and nothing else - that is what stops one plugin's resources
from overwriting Macro Deck's own catalog or another plugin's.

## Counting things

A count-dependent sentence is one key, not a word glued onto a number. Declare the forms with `[plural]`
in the entry's comment, and name them by suffixing the key:

```xml
<data name="Icons.Count.One" xml:space="preserve">
  <value>{count} icon</value>
  <comment>[plural]</comment>
</data>
<data name="Icons.Count.Other" xml:space="preserve">
  <value>{count} icons</value>
  <comment>[plural]</comment>
</data>
```

**Every form carries the marker**, not just one of them. `.resx` has no notion of a group, so the
compiler decides per entry whether it is a form; if only `Other` were marked and its siblings inferred,
a family that carries *only* `One` - a real typo - would be indistinguishable from an ordinary key, and
[MDLOC007](#mdloc007) could never report it.

The two entries generate **one** member at the base key, taking the count first:

```csharp
public static LocalizedString Count(int count);   // Strings.Icons.Count(3)
```

The reference it returns carries the base key, `Icons.Count`; whoever resolves it picks the form. So a
client holding that reference re-picks the form when the language changes, exactly as it re-resolves any
other key.

`Other` is required - it is the form every count other than one resolves through, and the one a language
with no singular/plural distinction would use. `One` is optional. A family missing `Other`, or one whose
key does not end in a form name, is [MDLOC007](#mdloc007).

**A form may leave the count out of its own text** - `one icon` against `{count} icons` - and that is not
a placeholder mismatch. A caller cannot know which form its number will select, so `count` is always a
parameter even when the selected form does not print it, and a form is checked against the family's
placeholders rather than against the other form's.

Plural is opt-in precisely so keys that merely happen to be named `.One` and `.Other` keep generating the
ordinary members they always did.

### Only `One` and `Other`

The rule is `count == 1`, for every culture. That is deliberately not CLDR. The same choice has to be made
identically by the host's C# resolver, the Angular clients and the desktop bootstrapper, and .NET ships no
plural-rule data - so a CLDR implementation would mean three hand-maintained copies of a large rule set
kept in step by hope. One small rule that is visibly the same in all three fails less quietly. It is
exactly correct for English, German, Italian, Spanish and French, which all select on `n == 1`. Czech and
Polish do not - both need `few`/`many` forms this model has no room for - so a plugin targeting those
locales should phrase its `Other` form to avoid noun-count agreement (a count-agnostic label rather than a
declined noun) so the same text stays grammatical across every count, rather than relying on a form that
is only correct for one CLDR bucket. A language that cannot be phrased this way needs the closed set
extended first, which is what makes the form names a build-time check rather than free text.

## The fallback chain

Resolving a reference tries, in order: the requested culture, its neutral culture, the catalog's own
default language, then Macro Deck's own default (`en`). Duplicates collapse, so `de-DE` against a
catalog whose default language is `de` tries `de-DE, de, en`.

A key no culture in the chain carries never renders blank. It resolves to `[[scope:Key]]` - for example
`[[plugin:com.example.spotify:Configuration.Title]]` - deliberately conspicuous, because a blank label
reads as a rendering bug where the bracketed key names exactly what is missing and which catalog should
have carried it. You will see this shape for a plugin whose catalog has not arrived at the host yet, or
for a key a plugin update removed.

Macro Deck's active language is a single value the host is the sole authority over - there is no
per-client culture. A plugin never needs to ask what language it is; it only ever produces references,
declares which cultures it ships and hands over their catalogs on request. See the
[`localization` capability kind](/reference/websocket/#capabilities) for exactly how the host asks a
plugin for that.

That holds for your action names, parameter labels, event and issue descriptors and config-flow text as
well as for text inside a Macro Deck UI tree: assign a generated `Strings.*` member and it reaches the
reader in the reader's language. Against a host older than protocol v3 the SDK resolves the reference in
your own default language before sending it, because those hosts type the field as a plain string - you
do not have to check for this, but it is why an old host shows your action names the way you wrote them.

One field is deliberately not localized. `ConfigFlowResult.Complete(title, …)` takes a plain string,
because the host stores that title as the name of the configured entry and the user can rename it from
there. Write it in your default language and leave it alone.

## The manifest `languages` field

The catalog above is what a *running* plugin serves. A store, an update listing or the plugin browser
needs the same information one step earlier - before anything is installed - so the manifest carries it
too, as [`languages`](/reference/manifest/#languages):

```json
"languages": ["en", "de", "zh-Hant-TW"]
```

You do not maintain that list. `macrodeck-plugin build` and `macrodeck-plugin pack` derive it from the
resource set this guide describes, using the same rules the generator does:

- an unsuffixed `Strings.resx` contributes `en`, the default language every catalog falls back to;
- each `Strings.<culture>.resx` contributes its own culture, whole - `pt-BR` stays `pt-BR`;
- a suffix [MDLOC005](#mdloc005) would reject never reaches the manifest.

Tags are BCP-47 and are never shortened to two letters: `zh-Hans` and `zh-Hant` are different languages
to a reader, and both would collapse onto `zh`. Anything that wants a coarser grouping can compute it
from the full tag.

Deriving it needs the project tree, because a culture-suffixed `.resx` never becomes a satellite assembly
here - it is compiled into the generated catalog, so a published output has nothing left to read. That is
the one case where a hand-written `languages` is carried through instead: packing a payload directory
that has no `Localization/` in it. Where both exist and disagree, the resource files win and `pack`
reports what it replaced.

## Diagnostics

Every diagnostic here is reported by the same source generator that produces `Strings` - there is
nothing separate to enable, and no diagnostic fires without a `Localization/*.resx` present to check.

### MDLOC001

**A key exists in a translation but not in the default-language file.** The default-language file is
what every translation is checked against and the last resource the fallback chain tries, so a key that
lives only in `Strings.de.resx` can never resolve for a reader on any other language. Add the key to
`Strings.resx` too, even with placeholder default-language text, or remove it from the translation if it
was added by mistake.

### MDLOC002

**A translation's placeholders differ from the default language's.** Placeholders are named, and the
generated method's parameters come from the default-language template - a translation naming a
placeholder the default language does not have would render that placeholder literally for every reader
on that language, and one silently dropping a placeholder would lose the value a caller passed. Make the
translation use exactly the same placeholder names as the default-language entry; order does not matter.

### MDLOC003

**The same key is declared twice in one resource file.** Which value wins is left up to file order, an
implementation detail nobody should depend on. Rename one entry or delete the duplicate.

### MDLOC004

**A parameter type declared in a resource comment is not usable** - either the bracketed type name is
not one of `string`, `int`, `long`, `double` or `bool`, or it declares a type for a placeholder the
template does not actually use. Fix the declared type, or remove a declaration for a placeholder you
renamed or deleted.

### MDLOC005

**A resource file's culture suffix is not a well-formed culture name.** `Strings.de_DE.resx` (underscore
instead of hyphen) or a name with the wrong segment shape fails this check even though .NET might
otherwise accept it as *some* culture - the check is on the name's shape specifically so a typo becomes
a build error instead of a culture no reader ever asks for. Rename the file to a valid BCP-47 name such
as `de-DE`.

### MDLOC006

**Code references a Macro Deck catalog key that has been removed.** Macro Deck's own catalog
(`MacroDeckStrings`) is a published contract: a key it retires is recorded rather than deleted outright,
the same way an SDK API is deprecated rather than pulled out from under you - see
[deprecations](/policies/deprecations/) for the equivalent lifecycle on the rest of the SDK. Update the
call site to the replacement the diagnostic names.

A key is retired by prefixing its comment with `[removed:…]`, alongside any parameter declarations:

```xml
<data name="Common.Submit" xml:space="preserve">
  <value>Submit</value>
  <comment>[removed:Use Common.Save instead.] Retired in 3.1.</comment>
</data>
```

The member is still generated and the key still resolves, so existing call sites keep compiling and keep
rendering text. Only the diagnostic is new - deleting the key outright would leave every call site with a
bare "member does not exist" error and nothing to migrate to.

### MDLOC007

**A plural family cannot produce a usable member** - an entry marked `[plural]` whose key does not end in
`One` or `Other`, a family with no `Other` form to fall back on, or a base key some ordinary entry already
owns. Rename the entry to end in a form the framework selects, add the missing `Other`, or rename whichever
of the two collides.

### MDLOC008

**A key is also the group other keys nest under** - `Filters` alongside `Filters.Date`. A dotted key
becomes a nested class, so the two would generate a method and a class of the same name in the same
scope. Rename one of them; `FiltersHeading` beside `Filters.Date` is the usual fix.

## Editing with Rider's Localization Manager

`.resx` was chosen as the canonical format specifically because it is the one format [JetBrains Rider's
Localization Manager](https://www.jetbrains.com/help/rider/Localizing_Applications.html) understands -
no other resource format Macro Deck considered has equivalent tooling. Opening `Localization/Strings.resx`
in Rider gets you:

- **A culture grid** - every key as a row, every `Strings.<culture>.resx` as a column, edited in one
  place instead of file by file.
- **Missing-translation highlighting** - a cell Rider flags before you ever run a build, ahead of
  [MDLOC001](#mdloc001).
- **CSV export and import** - hand a translator a spreadsheet instead of an XML file, and bring their
  edits back into the grid.
- **Coordinated rename** - renaming a key updates it across every culture's file at once, instead of the
  default-language file drifting out of sync with its translations one entry at a time.

Nothing about the generator or the diagnostics requires Rider - the files are plain `.resx`, editable by
hand or through any other tool that produces well-formed `.resx` - but the Localization Manager is the
recommended workflow because it is the tooling this format was picked to unlock.

## See also

- [SDK reference](/reference/sdk-packages/) - the `MacroDeck.Localization` package.
- [Analyzers](/reference/analyzers/) - the full MDLOC table alongside every other diagnostic.
- [WebSocket reference](/reference/websocket/#capabilities) - the `localization` capability kind a
  remote plugin implements to hand its catalog to the host.
- [Theming](/ui/concepts/theming/) - `UiText` and where a `LocalizedString` is accepted on a view.
- [Manifest reference](/reference/manifest/#languages) - the `languages` field packing derives from your
  resource set.
- [Compatibility policy](/policies/compatibility/) - what is frozen about the localization surfaces.
