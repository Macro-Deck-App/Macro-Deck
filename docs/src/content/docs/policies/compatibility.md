---
title: Compatibility policy
description: Which Macro Deck plugin surfaces are frozen, what counts as a breaking change, how contracts are evolved additively, and how protocol majors are negotiated.
---

A plugin ships as a compiled assembly, built against an SDK version its author does not control, and is
updated on a schedule its author does not control either. The whole plugin model only works if a plugin
built today keeps working against a host shipped later.

That is the promise this page states: **a plugin compiled against an older SDK keeps loading and
behaving the same against a newer host - no recompile, no behaviour change.**

## What is frozen

Every contract below is frozen while it is public and not marked `[Obsolete]`. It will not be removed
or changed in an incompatible way outside the process described under
[how contracts change](#how-contracts-change).

**The packages you compile against:**

- `MacroDeck.Sdk`
- `MacroDeck.Plugin.Hosting`
- `MacroDeck.Plugin.Testing`
- `MacroDeck.Plugin.Packaging`
- `MacroDeck.Plugin.Serilog`
- `MacroDeck.Plugin.Cli`
- `MacroDeck.Ui`, `MacroDeck.Ui.Model`, `MacroDeck.Ui.Testing`
- `MacroDeck.Localization`

**The wire contract:**

- `MacroDeck.Plugin.Protocol` - the envelope, the DTOs, the message types, the error codes
- The `/api/plugins/*` endpoints and `/plugins/ws`
- The capability kind and host API name catalogues - including `device-provider`/`devices`,
  `layout-provider`/`layouts`, `folder-view-provider`/`folder-views` and
  `widget-type-provider`/`widget-types` - see
  [capability operations](/reference/protocol/#capability-operations)
- See [the protocol reference](/reference/protocol/) and
  [ADR 0026](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0026-plugin-protocol-and-sdk-boundary.md)

**The formats and identifiers:**

- The manifest and `.macroDeckPlugin` package format - see
  [the manifest reference](/reference/manifest/) and
  [ADR 0029](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0029-plugin-packaging-installation-and-supervision.md)
- The analyzer diagnostic ids (`MDP1001`, …), including the `MDLOC` family (`MDLOC001`-`MDLOC008`) -
  see [analyzers](/reference/analyzers/)
- The conformance check ids (`MDC0305`, …) - see [conformance](/reference/conformance/)
- The `.resx` resource contract a plugin's `Localization/*.resx` is compiled against - the
  default-language file, the `Strings.<culture>.resx` naming convention, named placeholders, and the
  bracketed parameter-type declaration in a `<comment>` - see [Localization](/features/localization/)
- Macro Deck's own localization catalog keys (`macrodeck:Common.Save`, …), exposed as
  `MacroDeckStrings`. **Additive-only**: a key already shipped is never deleted outright. Retiring one
  goes through the same idea as SDK deprecation - the key keeps resolving, referencing it from a plugin
  is reported as [MDLOC006](/reference/analyzers/#mdloc006) naming the replacement, and the record stays
  for as long as the deprecation lifecycle requires. See [deprecations](/policies/deprecations/) for the
  lifecycle this mirrors.

## What counts as a breaking change

**Source *and* binary compatibility both count.** A plugin ships compiled, so a change that recompiles
cleanly can still break it at run time. All of the following are breaking:

| Category | Examples |
| --- | --- |
| Removal or renaming | A public type, member, parameter, enum value, constant, protocol message type, field or error code |
| Signature change | A parameter or return type, parameter order, adding a required parameter **even with a default** (a binary break), `class` to `struct`, adding or narrowing a generic constraint |
| Interface change | Adding a member to an existing public interface without a default implementation, or making an existing member abstract |
| Validation change | Making a previously optional field required, tightening validation, or changing a default value a plugin relied on |
| Observable behaviour | Ordering guarantees, id formats (the `integrationId::localId` shape), thrown exception types, timing and lifecycle contracts, or an error code's meaning |
| Serialisation | Renaming a JSON property, changing its type, changing an enum's wire representation |

Note the two that surprise people most. **Adding a parameter with a default value is a binary break** -
the compiler bakes the argument list into the call site, so an already-compiled plugin calls a method
that no longer exists. And **changing what an error code means** is a break even though nothing about
its spelling changed, because a plugin branching on it now branches wrongly.

## How contracts change

The rule is *add, do not change*.

- **New overloads**, rather than new parameters on an existing method.
- **New optional members** with a sensible default, rather than a required one.
- **New optional DTO fields.** Unknown fields are ignored on read, so an older plugin simply does not
  see what it was not built for.
- **New message types.** An unrecognised message type produces `UNKNOWN_MESSAGE_TYPE` and is never
  fatal, so a peer that has never heard of a new type reports it and carries on. This is what makes
  every additive protocol change backward-compatible by construction.
- **New capability interfaces** a plugin opts into, rather than new members on an existing one. An
  existing interface is extended only through a default interface implementation.

Anything that genuinely has to go follows the deprecation lifecycle: marked with `[Obsolete]` *and*
`[MacroDeckDeprecated]` carrying a declared removal version, registered so it appears in the registry
table, and it **keeps working unchanged** the whole time. Removal happens only in the major release its
`RemovedIn` names - never earlier, never in a minor or a patch. See
[deprecations](/policies/deprecations/) and
[ADR 0037](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0037-sdk-deprecation-is-declared-metadata.md).

You are told twice, in two places: at compile time as
[MDP5002](/reference/analyzers/#mdp5002), with the removal version and the replacement in the message,
and at run time in the desktop app, where the person running your plugin sees the same finding under
the same id. Nothing is deprecated in the shipped SDK today - the mechanism ships ahead of the first
deprecation deliberately.

## The 3.0 metadata retype

One break was taken deliberately before 3.0 shipped, and it is the only one on this page that is not
additive. The members that carry an action's, a parameter's and a config-flow step's user-facing text
changed type from `string` to `LocalizedText`:

`IActionDefinition.Name`/`.Description`, `ActionParameter.Label`/`.Description`/`.Placeholder` and its
factories, `ActionParameterOption.Label`, `ActionStateDefinition.Label`,
`WidgetTargetOptions.Label`/`.Description`, `ConfigFlowStep.Title`/`.Description`, `ConfigFlowLink.Label`,
`ConfigFlowCopyValue.Label`, `ConfigFlowInstruction.Text`, and `IIntegration.Name`.

`LocalizedText` converts implicitly from `string`, so **assigning** a literal is unchanged -
`Label = "Client ID"` still compiles. What breaks is **implementing** one of those interface members:
`public string Name => "…"` has to become `public LocalizedText Name => …`. That is a source break and a
binary break, and the rule on this page is otherwise "add, do not change".

It was taken because 3.0 had not been released: no plugin existed to break, and the alternative was a
permanent parallel surface - `Name` beside `NameLocalized` - that every plugin author would have had to
learn and every host code path would have had to check, forever. After 3.0 this page's ordinary rules
apply to these members like any other.

Two things did **not** change:

- **`VariableDefinition.Name` is still a `string`.** It is the variable's identity - the host derives a
  definition id from it and substitutes a template placeholder into it - so it cannot mean different
  things in different languages. Localized display text goes in the additive `DisplayName` property.
- **Descriptor metadata is localized from protocol v3.** `ActionDescriptorDto.Name`,
  `ActionParameterDto.Label`, the event and issue descriptors and the config-flow DTOs carry
  `LocalizedText`, so an out-of-process plugin's action names reach the reader's client as references and
  render in the reader's language. Below v3 the SDK resolves them in the plugin's own default language
  first, because a host that predates the shape would reject an object where it expects a string - the
  negotiated version decides, and v1 and v2 are still served. A plugin's text inside a Macro Deck UI tree
  was never affected; it always travelled as a reference.

  `ConfigFlowResultDto.EntryTitle` stays a plain string on purpose: the host stores it as the configured
  entry's name, which the user then owns and can rename.

## Protocol majors

The protocol version is a **single monotonically increasing integer major, not semver**. Because
unknown fields are ignored and unknown message types are reported rather than fatal, every additive
change is backward-compatible without a version change - so a bump only ever means *breaking*, which is
exactly what a semver triple's minor and patch would fail to express.

Today `minimum` is `1` and `current` is `3`.

Major `2` changes exactly one thing: a widget appearance change names the states it applies to by
stable id rather than by the old fixed `Current`/`On`/`Off`/`Both` selector, because a button may now
have any number of states. Everything else in `2` is identical to `1`, and major `1` remains fully
served - see the [migration guide](/policies/migrations/) for what a version `1` plugin sees when it
restyles a button that has three states.

Major `3` changes exactly one thing: a plugin's action names, parameter labels and config-flow text may
be a `{"$localized":…}` reference the reader's client resolves, instead of a plain string in the plugin's
own language. Everything else in `3` is identical to `2`, and majors `1` and `2` remain fully served - a
plugin below `3` must keep sending a plain string, since a host that predates the shape would reject the
object.

Note what did **not** need a major: actions gained the ability to supply a button's states, and that
is an additive descriptor field plus an additive capability operation, so a plugin speaking version
`1` can be a state provider. Additive changes never move the major, however visible the feature.

The `variables` capability is the sharper version of that lesson, because it cuts both ways. Its catalog
half - `discover`, `resolve`, `subscribe` and the `variable-values` host API's `value`/`invalidate` -
added no message type: they are ordinary `capability.invoke`/`capability.result` and
`host.invoke`/`host.result` traffic in the same envelope every other capability and host API already
uses, so none of them moved the major. Then variables gained attributes on their definitions, a `write`
capability and a `set` operation, and *that* changed the shape of a payload a plugin already sends - so
the **capability** version moved from `1` to `2` while `ProtocolVersions.Current` stayed at `3`.

That is the distinction to hold on to: a capability version and the protocol major are negotiated
separately and break separately. A kind the host does not know, or one whose version range does not
overlap the host's, is rejected non-fatally at capability negotiation and the session proceeds degraded;
an unknown host API call is answered with `CapabilityUnsupported` rather than failing the session; and a
plugin that never declares a kind is simply never asked about it.

Localization is the other half of that lesson: it moved the **UI model** major without moving this one -
see [the localization major](#the-localization-major) below.

Negotiation happens exactly once, in `POST /api/plugins/sessions`. The plugin sends the range it
speaks; the host computes `negotiated = min(clientMax, current)` and fails with
`PROTOCOL_VERSION_UNSUPPORTED` when that falls below `max(clientMin, minimum)`, echoing its own
supported range so the plugin can report something actionable. The later `session.hello` only *asserts*
the outcome - it never re-negotiates.

**A protocol break means bumping the major and keeping the previous version served for the negotiated
range.** Version `1` is never silently redefined. In practice that means a plugin declaring
`{ minimum: 1, maximum: 1 }` in its manifest's `compatibility.protocol` keeps negotiating version 1
against a host that has moved on to 2, until 1 leaves that host's supported range - at which point the
failure is explicit and diagnosable rather than a subtly misbehaving session.

Capabilities negotiate independently, per kind, on the same min/max algorithm but with a **non-fatal**
failure policy: an unsupported or unknown capability kind is rejected with a reason and the session
proceeds degraded rather than failing outright.

### The localization major

Macro Deck UI text properties becoming localizable moves **`MacroDeck.Ui.Model`**
(`UiModelVersions.Current`) from `1` to `2`. A text-bearing configuration property may now carry a
localization reference where it previously always carried a JSON string, so a producer built against
version `1` and one built against version `2` are not interchangeable and the session has to say which it
speaks.

**Localization did not move the plugin protocol's major.** It is at `2` for an unrelated reason - widget
state by stable id, above - and localization would have left it at `1`. The two majors have never been
coupled (see
[ADR 0038](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0038-ui-model-and-declarative-dsl.md)),
and localization broke neither wire: the `localization` capability kind and its limits are additive, and a
localized value reads a plain JSON string as literal text, so every payload the protocol accepted before
is still accepted.

**Version `1` of the UI model stays served**: the host opens every session at the current version and
honours whatever a provider negotiates down to, so a plugin still on version `1` keeps emitting plain
strings and keeps working untouched. See
[ADR 0057](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0057-localization-is-a-deferred-reader-resolved-reference.md)
for the full record, including why this could not be done additively.

### The component major

Renaming the component vocabulary moves **`MacroDeck.Ui.Model`** (`UiModelVersions.Current`) from `2` to
`3`. Every node type in the profile was respelled: the ten a reader can draw from the tree alone became
`ui.*`, and the four that resolve a Macro Deck reference became `macrodeck.*`.

**This one also moved `UiModelVersions.Minimum`, which no previous change has done.** The rename is a
hard cut - no `widget.*` spelling is accepted, and no shim is published - so the package genuinely no
longer speaks majors `1` or `2`. Continuing to advertise them would be worse than refusing them: node
types are open and an unknown one is non-fatal, so a version-`2` tree would negotiate successfully and
then render every node as the unsupported placeholder. A blank view and a working session is exactly the
silent failure a negotiated major exists to turn into an honest refusal.

**Majors `1` and `2` of the UI model are therefore no longer served.** This was affordable exactly once,
and only because nothing depended on them yet: no plugin serves a component surface, and a UI tree is
built per session and never persisted, so there is no stored data to migrate. See
[ADR 0064](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0064-components-are-a-registry-over-two-namespaces.md)
for the full record, including why the two namespaces are split where they are.

## What "maintained per major" means in practice

- **Within an SDK major**, nothing frozen is removed or changed incompatibly. A minor or patch may add,
  and may deprecate; it may not remove. Upgrading the SDK inside a major should never require a source
  change to your plugin.
- **At an SDK major**, only APIs whose `[MacroDeckDeprecated]` named *that* major as their `RemovedIn`
  disappear. You will have had at least one release cycle of compile-time and run-time warnings naming
  the replacement.
- **Within a protocol major**, the error-code list and the message-type catalogue are append-only.
- **At a protocol major**, the previous major stays negotiable for as long as the host's supported
  range says it does. Check the descriptor at `GET /api/plugins/protocol`, which needs no
  authentication, rather than hard-coding a version.
- **Limits and timeouts are advertised, not fixed.** They are published in the protocol descriptor and
  again in the session response. Read them at run time; a value changing is not a breaking change, but
  hard-coding one makes it look like one.

## Verifying compatibility yourself

- Run the **conformance suite** against your plugin - `macrodeck-plugin test`. A broken check id is a
  break; the ids are stable, so gate CI on them. See [conformance](/reference/conformance/).
- Reference **`MacroDeck.Plugin.Analyzers`**. Its source generator records the deprecated APIs your
  compilation actually references, which is what lets the host report *confirmed* rather than merely
  *inferred* usage. A plugin without it lands in `inferred`, and nothing about it is ever presented to
  a user as fact.
- Check the **compatibility report** the host returns in the session response: an overall state, the
  evidence source behind it, and per-finding guidance. The states, from best to worst, are
  `compatible`, `deprecated_apis`, `update_recommended`, `update_required`, `partially_incompatible`,
  `incompatible`.

## If a fix cannot be made without a break

It is not made. A change that breaks a non-obsolete public contract is not shipped as a bug fix, is not
made unilaterally, and does not arrive in a minor release. It goes through deprecation, or through a
protocol major, or it does not happen.

## See also

- [Deprecations](/policies/deprecations/) - the lifecycle, the registry, and the evidence model.
- [Migrations](/policies/migrations/) - what a migration guide will contain, and when one is published.
- [Conformance](/reference/conformance/) - the contract suite and its stable check ids.
- [Plugin protocol](/reference/protocol/) - version and capability negotiation in detail.
