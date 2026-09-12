---
title: Localization
description: Translate a plugin with Localization/*.resx - the generated Strings class, placeholders, plurals, fallback, and the MDLOC diagnostics.
---

You write `Localization/Strings.resx` plus one `Strings.<culture>.resx` per language. The build turns them
into a typed `Strings` class whose members return a `LocalizedString`, and you assign that anywhere the SDK
takes `LocalizedText`. The host resolves it in the reader's language.

## Quick start

`Localization/Strings.resx` - the default language:

```xml
<data name="Actions.LogMessage.Name" xml:space="preserve">
  <value>Write log message</value>
</data>
<data name="Actions.LogMessage.Message.Label" xml:space="preserve">
  <value>Message</value>
</data>
```

`Localization/Strings.de.resx` - German:

```xml
<data name="Actions.LogMessage.Name" xml:space="preserve">
  <value>Log-Nachricht schreiben</value>
</data>
```

The project references the generator and the runtime package (the plugin template already does this):

```xml
<PackageReference Include="MacroDeck.Plugin.Analyzers" PrivateAssets="all" />
<PackageReference Include="MacroDeck.Localization" />
```

Use the generated members:

```csharp
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

public sealed class LogMessageAction : IActionDefinition
{
	public string Id => "log-message";

	public LocalizedText Name => Strings.Actions.LogMessage.Name();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Text("message", label: Strings.Actions.LogMessage.Message.Label(), required: true),
	];

	// Other members omitted.
}
```

A German reader sees **Log-Nachricht schreiben**; everyone else sees **Write log message**. The label has
no German entry, so German readers get **Message**.

- **The folder and base name matter.** The analyzer globs `Localization/**/*.resx`, and the base name
  `Strings` becomes the class name.
- **`Strings.resx` is always required**, even for a one-language plugin: every translation is checked
  against it, and the fallback chain ends there.
- **A dotted key is a nested class.** `Actions.LogMessage.Name` becomes `Strings.Actions.LogMessage.Name()`.
- **A translation may leave keys out.** A missing key falls back - see
  [Choosing the language](#choosing-the-language).

## Placeholders

```xml
<data name="Status.ConnectedAs" xml:space="preserve">
  <value>Connected as {userName}</value>
</data>
<data name="Status.Retries" xml:space="preserve">
  <value>Retry {attempt} of {limit}</value>
  <comment>[attempt:int][limit:int] Shown while reconnecting.</comment>
</data>
```

```csharp
Strings.Status.ConnectedAs(userName: connection.User);  // Connected as manuel
Strings.Status.Retries(attempt: 2, limit: 5);           // Retry 2 of 5
```

- A placeholder is a bare name in braces, matched by name, not position - a translation may reorder them.
- Parameters come from the **default-language** template only. A translation must use exactly the same
  names ([MDLOC002](#mdloc002)).
- An undeclared placeholder is `LocalizedText`, so a string literal works and a `Strings.*` member can be
  nested - it resolves in the same language as the outer sentence.
- Declare a narrower type as a bracketed prefix on `<comment>`: `string`, `int`, `long`, `double` or
  `bool`. The rest of the comment stays a translator note. A bad type, or a type for a placeholder the
  template does not use, is [MDLOC004](#mdloc004).
- Values are formatted culture-invariantly: no thousands separators, `.` as decimal point, lowercase
  booleans.
- `{{` and `}}` escape to one brace. So a value written as `{{ vars.x }}` reaches the user as `{ vars.x }`.
- A placeholder that gets no value stays literal in the output (`{deviceName}`), not blank.

## Plurals

```xml
<data name="Status.Scenes.One" xml:space="preserve">
  <value>{count} scene</value>
  <comment>[plural]</comment>
</data>
<data name="Status.Scenes.Other" xml:space="preserve">
  <value>{count} scenes</value>
  <comment>[plural]</comment>
</data>
```

```csharp
Strings.Status.Scenes(3);  // one member at the base key, count first
```

| count | en | de (`eine Szene` / `{count} Szenen`) |
| --- | --- | --- |
| `1` | 1 scene | eine Szene |
| `0` | 0 scenes | 0 Szenen |
| `3` | 3 scenes | 3 Szenen |

- **Mark every form** with `[plural]`, not just one. Plural is opt-in, so keys that just happen to end in
  `.One`/`.Other` stay ordinary members.
- `Other` is required and `One` optional. A missing `Other`, a marked key not ending in `One`/`Other`, or a
  base key an ordinary entry already owns is [MDLOC007](#mdloc007).
- A form may leave `{count}` out of its text (`eine Szene`). `count` is always a parameter, and each form
  is checked against the family's placeholders, not the other form's.
- The reference carries the base key (`Status.Scenes`). The reader's side picks the form, and picks it
  again after a language change.

### Only `One` and `Other`

The rule is `count == 1` for every culture. It is not CLDR: the host, the Angular clients and the
bootstrapper must all pick the same form. The rule is exact for English, German, Italian, Spanish and
French. Czech and Polish need `few`/`many` forms that this model does not have. For those, phrase `Other`
so that it avoids noun-count agreement: use a count-agnostic label rather than a declined noun. A language
that cannot be phrased that way needs the closed set of forms extended first.

## Adding a language

```
Localization/
  Strings.resx            # default language (manifest: en)
  Strings.de.resx         # German
  Strings.pt-BR.resx      # Portuguese, Brazil
  Strings.zh-Hant-TW.resx # Traditional Chinese, Taiwan
```

- The suffix must be a well-formed BCP-47 name. The check is on its shape, not on whether .NET knows the
  culture, so `Strings.de_DE.resx` fails with [MDLOC005](#mdloc005).
- Every key in a translation must also exist in `Strings.resx` ([MDLOC001](#mdloc001)).
- Nothing else to register - the next build compiles the culture into the generated catalog and adds it to
  the manifest's [`languages`](#the-manifest-languages-field).

## Reuse `MacroDeckStrings` instead of duplicating common strings

```csharp
new UiHeading { Key = "confirm", Text = MacroDeckStrings.Common.Save() };

// "{field} is required" with your own label nested in it
ActionResult.Failed(ActionErrorCodes.InvalidParameter,
	MacroDeckStrings.Validation.Required(Strings.Actions.LogMessage.Message.Label()));
```

Macro Deck ships a reusable catalog, generated the same way into `MacroDeckStrings` and already translated
into every language the app carries: `Common.*` (`Save`, `Cancel`, `Delete`, `Retry`, ...), `States.*`
(`On`, `Off`, `Muted`, `Active`, ...), `Validation.*` and more. Use it for anything generic rather than
adding your own `Save` key. Its entries live in scope `macrodeck`, not `plugin:<your-id>`.

That catalog is a published contract: keys are only ever added. A retired key is marked, not deleted - it
still compiles and resolves, and [MDLOC006](#mdloc006) points to the replacement.

## Localized text in results, issues and flows

```csharp
ActionResult.Failed(ActionErrorCodes.InvalidParameter, Strings.Errors.MissingMessage());
VariableWriteResult.Unavailable(Strings.Errors.NotConnected());
IssueResolution.Failed(Strings.Issues.TokenExpired());

ConfigFlowResult.Complete(title: "Studio PC");  // plain string - not localized
```

Every user-facing SDK member typed `LocalizedText` takes a `Strings.*` member: action and parameter text,
event and issue descriptors, result messages, config-flow text, UI trees. `LocalizedText` also takes a
plain string for text that is already final, such as a device name. A `LocalizedString` does not convert to
`string` - assigning one to a `string` is a compile error.

`ConfigFlowResult.Complete`'s title is the one deliberate exception. The host stores it as the configured
entry's name, which the user can rename. Write it in your default language.

## Choosing the language

The host holds one active language, the one set in Macro Deck. There is no per-client culture, and a plugin
never asks for it. The host resolves each reference by trying the cultures in this order:

| step | `de-AT` reader, plugin default `en` |
| --- | --- |
| 1. requested culture | `de-AT` |
| 2. its neutral culture | `de` |
| 3. the catalog's default language | `en` |
| 4. Macro Deck's default | `en` (duplicate, collapsed) |

A key that no culture carries renders as `[[scope:Key]]`, never blank - for example
`[[plugin:com.example.demo:Status.Scenes]]`. You see this when a key was removed by an update, or when the
plugin's catalog has not reached the host yet.

Against a host older than protocol v3, the SDK resolves descriptor text (action names, labels) in your
default language before sending it. You do not need to handle this case.

## Testing translations

```csharp
using MacroDeck.Localization;

[Test]
public void German_readers_see_german_text()
{
	var registry = new LocalizationCatalogRegistry();
	registry.Register(Strings.LocalizationCatalog);
	registry.Register(MacroDeckStrings.LocalizationCatalog);
	var resolver = new LocalizationResolver(registry);

	Assert.That(resolver.Resolve(Strings.Status.ConnectedAs("manuel"), "de"), Is.EqualTo("Verbunden als manuel"));
	Assert.That(resolver.Resolve(Strings.Status.Scenes(1), "de"), Is.EqualTo("eine Szene"));
	Assert.That(resolver.Resolve(Strings.Status.Scenes(3), "fr"), Is.EqualTo("3 scenes")); // falls back
}
```

Structural mistakes - a missing default key, mismatched placeholders, a broken plural family - are already
build diagnostics, so test the wording that matters, not every key.

For bulk editing, [Rider's Localization
Manager](https://www.jetbrains.com/help/rider/Localizing_Applications.html) shows every key against every
culture in one grid, highlights missing translations, renames a key across all files, and round-trips CSV for
translators. The files are plain `.resx`, so any tool works.

## The manifest `languages` field

```json
"languages": ["en", "de", "pt-BR", "zh-Hant-TW"]
```

`macrodeck-plugin build` and `pack` derive [`languages`](/reference/manifest/#languages) from the resource
set - you do not maintain it.

- An unsuffixed `Strings.resx` contributes `en`; each `Strings.<culture>.resx` contributes its culture,
  whole (`zh-Hans` and `zh-Hant` are never shortened to `zh`).
- A suffix that fails [MDLOC005](#mdloc005) never reaches the manifest.
- A hand-written `languages` is only carried through when packing a payload directory with no
  `Localization/` folder. Culture files are compiled into the generated catalog, not into satellite
  assemblies, so the built output has nothing left to read. If both exist and disagree, the resource files
  win and `pack` reports what it replaced.

## Reference

### The generated API

| resx | generated member |
| --- | --- |
| `Connect` | `static LocalizedString Connect()` |
| `Status.ConnectedAs` = `Connected as {userName}` | `Status.ConnectedAs(LocalizedText userName)` |
| `Status.Retries`, comment `[attempt:int][limit:int]` | `Status.Retries(int attempt, int limit)` |
| `Status.Scenes.One` / `.Other`, comment `[plural]` | `Status.Scenes(int count)` |
| (class) | `Strings.LocalizationScope` = `"plugin:<manifest id>"` |
| (class) | `Strings.LocalizationCatalog` - the compiled `ILocalizationCatalog` |

`Strings` is a `public static partial class` in your project. It returns a reference, not text, so a
language change never requires rebuilding your UI.

| MSBuild property | default |
| --- | --- |
| `MacroDeckLocalizationScope` | `plugin:<id>`, read from `manifest.json` |
| `MacroDeckLocalizationClassName` | the resource base name, `Strings` |

A plugin owns only `plugin:<its-own-id>`, so it cannot overwrite Macro Deck's catalog or another plugin's.

### Comment prefixes

| prefix | meaning |
| --- | --- |
| `[name:type]` | Placeholder type: `string`, `int`, `long`, `double`, `bool`. |
| `[plural]` | This entry is one form of a plural family. On every form. |
| `[removed:guidance]` | Retired key: still generated and resolvable, reports [MDLOC006](#mdloc006). |

### Diagnostics

All reported by the generator that produces `Strings`. None fire without a `Localization/*.resx`.

#### MDLOC001

A key is in a translation but not in `Strings.resx`. Add it to the default file, or remove it from the
translation.

#### MDLOC002

A translation's placeholder names differ from the default language's. Use exactly the same names; order
does not matter.

#### MDLOC003

The same key is declared twice in one file. Rename or delete one.

#### MDLOC004

A declared placeholder type is not `string`, `int`, `long`, `double` or `bool`, or it names a placeholder
the template does not use.

#### MDLOC005

A file's culture suffix is not a well-formed BCP-47 name (`Strings.de_DE.resx`). Rename it, for example to
`de-DE`.

#### MDLOC006

Code uses a `MacroDeckStrings` key retired with `[removed:...]`. Move to the replacement the message names;
see [deprecations](/policies/deprecations/).

```xml
<data name="Common.Submit" xml:space="preserve">
  <value>Submit</value>
  <comment>[removed:Use Common.Save instead.] Retired in 3.1.</comment>
</data>
```

#### MDLOC007

A plural family cannot produce a member: a marked key not ending in `One`/`Other`, no `Other` form, or a
base key an ordinary entry already owns.

#### MDLOC008

A key is also a group other keys nest under (`Filters` beside `Filters.Date`), so it would generate a
method and a class with the same name. Rename one, for example to `FiltersHeading`.

## See also

- [SDK packages](/reference/sdk-packages/) - the `MacroDeck.Localization` package.
- [Analyzers](/reference/analyzers/) - the MDLOC table alongside every other diagnostic.
- [WebSocket reference](/reference/websocket/#capabilities) - the `localization` capability kind a remote
  plugin uses to hand its catalog to the host.
- [Theming](/ui/concepts/theming/) - `UiText` and where a view accepts a `LocalizedString`.
- [Manifest reference](/reference/manifest/#languages) - the `languages` field.
- [Compatibility policy](/policies/compatibility/) - what is frozen about localization.
