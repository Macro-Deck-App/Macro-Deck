---
title: Compatibility policy
description: Which Macro Deck plugin surfaces are frozen, what counts as a breaking change, how contracts are evolved additively, and how protocol majors are negotiated.
---

A plugin ships as a compiled assembly, built against an SDK version and updated on a schedule its author
controls, not Macro Deck. So the promise is: **a plugin compiled against an older SDK keeps loading and
behaving the same against a newer host - no recompile, no behaviour change.** Every contract below is
frozen while it is public and not marked `[Obsolete]`, and changes only through the process under
[how contracts change](#how-contracts-change).

## What is covered

| Surface | Examples | Promise |
| --- | --- | --- |
| SDK packages | `MacroDeck.Sdk`, `MacroDeck.Plugin.Hosting`, `MacroDeck.Plugin.Testing`, `MacroDeck.Plugin.Packaging`, `MacroDeck.Plugin.Serilog`, `MacroDeck.Plugin.Cli`, `MacroDeck.Localization` | No public type or member is removed or changed incompatibly, source or binary. |
| UI model | `MacroDeck.Ui`, `MacroDeck.Ui.Model`, `MacroDeck.Ui.Testing` | Frozen like the SDK; the UI model's wire major is negotiated separately - see [the UI model majors](#ui-model-majors). |
| Protocol | `MacroDeck.Plugin.Protocol`: the envelope, DTOs, message types, error codes | Append-only within a protocol major; a break needs a new major. |
| Plugin HTTP and WebSocket | `/api/plugins/*`, `/plugins/ws` | Existing endpoints keep their shape and meaning - see [the protocol reference](/reference/protocol/). |
| Capability and host API catalogues | `device-provider`/`devices`, `layout-provider`/`layouts`, `folder-view-provider`/`folder-views`, `widget-type-provider`/`widget-types` | Names stay; see [capability operations](/reference/protocol/#capability-operations). |
| Manifest and package format | `manifest.json`, the `.macroDeckPlugin` package | An existing manifest and package keep installing - see [the manifest reference](/reference/manifest/). |
| Analyzer diagnostic ids | `MDP1001`, …, the `MDLOC` family `MDLOC001`-`MDLOC008` | An id keeps its meaning and is never reused - see [analyzers](/reference/analyzers/). |
| Conformance check ids | `MDC0305`, … | Stable, so you can gate CI on them - see [conformance](/reference/conformance/). |
| Plugin `.resx` contract | the default-language file, `Strings.<culture>.resx`, named placeholders, the bracketed parameter type in a `<comment>` | A plugin's `Localization/*.resx` keeps compiling - see [Localization](/features/localization/). |
| Macro Deck's localization catalog | `macrodeck:Common.Save`, … exposed as `MacroDeckStrings` | **Additive-only**: a shipped key is never deleted outright. |

Retiring a catalog key follows the same idea as [SDK deprecation](/policies/deprecations/): the key
keeps resolving, a plugin referencing it gets [MDLOC006](/reference/analyzers/#mdloc006) naming the
replacement, and the record stays for as long as the deprecation lifecycle requires.

Background: [ADR 0026](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0026-plugin-protocol-and-sdk-boundary.md)
(protocol and SDK boundary) and
[ADR 0029](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0029-plugin-packaging-installation-and-supervision.md)
(packaging).

## Breaking or not?

**Source *and* binary compatibility both count.** A plugin ships compiled, so a change that recompiles
cleanly can still break it at run time.

| Change | Verdict |
| --- | --- |
| Remove or rename a public type, member, parameter, enum value, constant, message type, field or error code | Break |
| Change a parameter or return type, or parameter order | Break |
| Add a parameter to a public method, **even with a default value** | Binary break |
| Change `class` to `struct`, or add or narrow a generic constraint | Break |
| Add a member to a public interface without a default implementation, or make an existing member abstract | Break for every implementer |
| Add a member to a public interface **with** a default implementation | Allowed |
| Add a new overload | Allowed |
| Add a value to a C# enum | Allowed by .NET's own rules - give a `switch` over a Macro Deck enum a default branch |
| Add an optional DTO or manifest field | Allowed - unknown fields are ignored on read |
| Rename a JSON property, change its type, or change an enum's wire representation | Break |
| Make an optional field required, tighten validation, or change a default a plugin relied on | Break |
| Change an error code's meaning, even with the same spelling | Break |
| Change ordering guarantees, id formats (the `integrationId::localId` shape), thrown exception types, timing or lifecycle contracts | Break |

The two that surprise people most:

**A parameter with a default is a binary break.** The compiler bakes the full argument list into the
call site, so an already-compiled plugin calls a method that no longer exists and gets a
`MissingMethodException`.

```csharp
// illustrative method
// before
public Task ShowAsync(string text);

// after - recompiles cleanly, breaks every compiled caller
public Task ShowAsync(string text, TimeSpan? duration = null);

// instead - keep the old method, add an overload
public Task ShowAsync(string text);
public Task ShowAsync(string text, TimeSpan duration);
```

**A changed meaning is a break.** Nothing about the spelling changes, but a plugin branching on it now
branches wrongly.

```text
hypothetical: an error code that meant "retry later" starts to mean "give up"
same spelling, same field - still a break, because plugins that retry now retry wrongly
```

## How contracts change

The rule is *add, do not change*.

**New overloads**, rather than new parameters on an existing method - see the example above.

**New optional members** with a sensible default, rather than required ones.

**New optional DTO fields.** Unknown fields are ignored on read, so an older plugin simply does not see
what it was not built for.

```jsonc
// illustrative: a reader built before displayName existed reads this exactly as {"id":"a"}
{ "id": "a", "displayName": "Kitchen" }
```

**New message types.** An unrecognised message type produces `UNKNOWN_MESSAGE_TYPE` and is never fatal:
the peer reports it and carries on. That makes every additive protocol change backward-compatible by
construction.

**New capability interfaces** a plugin opts into, rather than new members on an existing one. An existing
interface is extended only through a default interface implementation.

```csharp
// illustrative interface names
// not this: every existing implementer stops loading
public interface IStateProvider { Task RefreshAsync(); }

// this: a new opt-in interface
public interface IRefreshableStateProvider : IStateProvider { Task RefreshAsync(); }

// or, on the existing interface, a default implementation
public interface IStateProvider { Task RefreshAsync() => Task.CompletedTask; }
```

**Anything that has to go follows the deprecation lifecycle.** It is marked with `[Obsolete]` *and*
`[MacroDeckDeprecated]` carrying a declared removal version, registered so it appears in the registry
table, and it **keeps working unchanged** the whole time. Removal happens only in the major release its
`RemovedIn` names - never earlier, never in a minor or a patch. You are told at compile time as
[MDP5002](/reference/analyzers/#mdp5002), with the removal version and replacement in the message, and at
run time in the desktop app, where the person running your plugin sees the same finding under the same
id. See [deprecations](/policies/deprecations/) and
[ADR 0037](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0037-sdk-deprecation-is-declared-metadata.md).

**A wire break means a new protocol major, with the previous one still served** - see
[protocol majors](#protocol-majors).

## The 3.0 metadata retype

One break was taken deliberately before 3.0 shipped, and it is the only non-additive change on this page.
The members carrying an action's, a parameter's and a config-flow step's user-facing text changed type
from `string` to `LocalizedText`:

`IActionDefinition.Name`/`.Description`, `ActionParameter.Label`/`.Description`/`.Placeholder` and its
factories, `ActionParameterOption.Label`, `ActionStateDefinition.Label`,
`WidgetTargetOptions.Label`/`.Description`, `ConfigFlowStep.Title`/`.Description`, `ConfigFlowLink.Label`,
`ConfigFlowCopyValue.Label`, `ConfigFlowInstruction.Text`, and `IIntegration.Name`.

```csharp
Label = "Client ID"                       // assigning: unchanged, LocalizedText converts from string
public string Name => "Timer";            // implementing: no longer compiles
public LocalizedText Name => "Timer";     // implementing: fixed
```

Implementing one of those members is a source and a binary break. It was affordable because 3.0 had not
been released and no plugin existed to break; the alternative was a permanent parallel surface (`Name`
beside `NameLocalized`). After 3.0 this page's ordinary rules apply to these members like any other.

Two things did **not** change:

- **`VariableDefinition.Name` is still a `string`.** It is the variable's identity - the host derives a
  definition id from it and substitutes it into a template placeholder - so it cannot differ by language.
  Localized display text goes in the additive `DisplayName` property.
- **Descriptor metadata is localized from protocol v3.** `ActionDescriptorDto.Name`,
  `ActionParameterDto.Label`, the event and issue descriptors and the config-flow DTOs carry
  `LocalizedText`, so an out-of-process plugin's action names render in the reader's language. Below v3
  the SDK resolves them in the plugin's own default language first, because an older host would reject an
  object where it expects a string - the negotiated version decides, and v1 and v2 are still served. A
  plugin's text inside a Macro Deck UI tree was never affected; it always travelled as a reference.
  `ConfigFlowResultDto.EntryTitle` stays a plain string on purpose: the host stores it as the configured
  entry's name, which the user then owns and can rename.

## Versions

| Version | Scheme | Breaks allowed |
| --- | --- | --- |
| SDK packages | Ordinary semver | Only at an SDK major, and only removals whose `RemovedIn` names that major |
| Plugin protocol | One monotonically increasing integer major, not semver | Only with a new major; the previous one stays served |
| Capability, per kind | Integer min/max range per kind | Only with a new capability version; negotiated separately from the protocol |
| UI model (`UiModelVersions`) | Integer major | Only with a new major; negotiated separately from the protocol |

### Protocol majors

Unknown fields are ignored and unknown message types are reported rather than fatal, so every additive
change is backward-compatible without a version change. A bump therefore only ever means *breaking*.

Today `minimum` is `1` and `current` is `3`.

| Major | Changes exactly one thing | Older majors |
| --- | --- | --- |
| `2` | A widget appearance change names its states by stable id instead of the fixed `Current`/`On`/`Off`/`Both` selector, because a button may have any number of states. | `1` fully served - see the [migration guide](/policies/migrations/) for what a version `1` plugin sees when it restyles a three-state button. |
| `3` | Action names, parameter labels and config-flow text may be a `{"$localized":…}` reference the reader's client resolves, instead of a plain string. | `1` and `2` fully served. A plugin below `3` must keep sending a plain string, since an older host would reject the object. |

What did **not** need a major:

- Actions gained the ability to supply a button's states: an additive descriptor field plus an additive
  capability operation, so a version `1` plugin can be a state provider. Additive changes never move the
  major, however visible the feature.
- The `variables` catalog half - `discover`, `resolve`, `subscribe` and the `variable-values` host API's
  `value`/`invalidate` - is ordinary `capability.invoke`/`capability.result` and
  `host.invoke`/`host.result` traffic. Variable definition attributes, a `write` capability and a `set`
  operation *did* change a payload plugins already send, so the **capability** version moved from `1` to
  `2` while `ProtocolVersions.Current` stayed at `3`.
- Localization moved the **UI model** major, not this one - see [the localization major](#the-localization-major).

**Negotiation happens exactly once**, in `POST /api/plugins/sessions`:

```text
plugin sends:  { minimum: clientMin, maximum: clientMax }
host computes: negotiated = min(clientMax, current)
fails with PROTOCOL_VERSION_UNSUPPORTED when negotiated < max(clientMin, minimum),
echoing the host's own supported range
```

The later `session.hello` only *asserts* the outcome - it never re-negotiates.

**A protocol break means bumping the major and keeping the previous version served for the negotiated
range.** Version `1` is never silently redefined. A plugin declaring `{ minimum: 1, maximum: 1 }` in its
manifest's `compatibility.protocol` keeps negotiating version 1 against a host that has moved on, until 1
leaves that host's supported range - at which point the failure is explicit and diagnosable.

**Capabilities negotiate independently**, per kind, on the same min/max algorithm but with a
**non-fatal** failure policy: an unknown kind, or one whose range does not overlap the host's, is
rejected with a reason and the session proceeds degraded. An unknown host API call is answered with
`CapabilityUnsupported` rather than failing the session, and a plugin that never declares a kind is never
asked about it.

### UI model majors

#### The localization major

Localizable Macro Deck UI text properties moved **`MacroDeck.Ui.Model`** (`UiModelVersions.Current`)
from `1` to `2`: a text-bearing configuration property may now carry a localization reference where it
always carried a JSON string, so the session has to say which version it speaks.

**Localization did not move the plugin protocol's major.** The protocol moved to `2` for an unrelated
reason (widget state by stable id) and localization would have left it at `1`. The two majors have never
been coupled (see
[ADR 0038](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0038-ui-model-and-declarative-dsl.md)).
Localization broke neither wire: the `localization` capability kind and its limits are additive, and a
localized value reads a plain JSON string as literal text.

**Version `1` of the UI model stays served**: the host opens every session at the current version and
honours whatever a provider negotiates down to, so a plugin on version `1` keeps emitting plain strings
untouched. See
[ADR 0057](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0057-localization-is-a-deferred-reader-resolved-reference.md).

#### The component major

Renaming the component vocabulary moved **`MacroDeck.Ui.Model`** (`UiModelVersions.Current`) from `2` to
`3`. Every node type was respelled: the ten a reader can draw from the tree alone became `ui.*`, the four
that resolve a Macro Deck reference became `macrodeck.*`.

```text
widget.*  ->  ui.*          (drawn from the tree alone)
widget.*  ->  macrodeck.*   (resolves a Macro Deck reference)
```

**This one also moved `UiModelVersions.Minimum`.** It is a hard cut - no `widget.*` spelling is
accepted, and no shim is published - so **majors `1` and `2` of the UI model are no longer served.**
Advertising them would be worse than refusing them: an unknown node type is non-fatal, so a version `2`
tree would negotiate and then render every node as the unsupported placeholder. This was affordable
exactly once, because no plugin served a component surface and a UI tree is built per session and never
persisted. See
[ADR 0064](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0064-components-are-a-registry-over-two-namespaces.md).

### What "maintained per major" means in practice

- **Within an SDK major**, nothing frozen is removed or changed incompatibly. A minor or patch may add,
  and may deprecate; it may not remove. Upgrading the SDK inside a major should never require a source
  change to your plugin.
- **At an SDK major**, only APIs whose `[MacroDeckDeprecated]` named *that* major as their `RemovedIn`
  disappear. You will have had at least one release cycle of compile-time and run-time warnings naming
  the replacement.
- **Within a protocol major**, the error-code list and the message-type catalogue are append-only.
- **At a protocol major**, the previous major stays negotiable for as long as the host's supported range
  says it does. Read the descriptor at `GET /api/plugins/protocol`, which needs no authentication, rather
  than hard-coding a version.
- **Limits and timeouts are advertised, not fixed.** They are published in the protocol descriptor and
  again in the session response. A value changing is not a breaking change; read them at run time.

## Verifying compatibility yourself

- Run the **conformance suite** against your plugin: `macrodeck-plugin test`. A broken check id is a
  break, and the ids are stable, so gate CI on them. See [conformance](/reference/conformance/).
- Reference **`MacroDeck.Plugin.Analyzers`**. Its source generator records the deprecated APIs your
  compilation actually references, so the host can report *confirmed* rather than *inferred* usage. A
  plugin without it lands in `inferred`, which is never presented to a user as fact.
- Check the **compatibility report** in the session response: an overall state, the evidence source, and
  per-finding guidance. States, best to worst: `compatible`, `deprecated_apis`, `update_recommended`,
  `update_required`, `partially_incompatible`, `incompatible`.

## If a fix cannot be made without a break

It is not made. A change that breaks a non-obsolete public contract is not shipped as a bug fix, is not
made unilaterally, and does not arrive in a minor release. It goes through deprecation, or through a
protocol major, or it does not happen.

## See also

- [Deprecations](/policies/deprecations/) - the lifecycle, the registry, and the evidence model.
- [Migrations](/policies/migrations/) - what a migration guide contains, and when one is published.
- [Conformance](/reference/conformance/) - the contract suite and its stable check ids.
- [Plugin protocol](/reference/protocol/) - version and capability negotiation in detail.
