# ADR 0012: Scripts are reusable flows with declared inputs and an owning widget

Status: Accepted

## Context

Every widget owns its action flows inside its own `Data` JSON. That is fine while a sequence belongs to
one button and falls apart as soon as it belongs to several: "start streaming" is a scene switch, a
mute, a notification and a folder change, copy-pasted into every widget that should do it, with no way
to tell which copies drifted ([#171](https://github.com/Macro-Deck-App/Macro-Deck/issues/171)).

The open question was never *how to run* a script — [ADR 0010](0010-one-flow-engine-for-triggers-events-and-automations.md)
already collapsed press and event triggers into one widget-less executor — but where a script lives, how
a widget names it, and what happens to that name when the setup moves to another machine.

Two things were deliberately left out of the first version and then asked for. A sequence differing by
one value — a scene name, a volume — could not be expressed as one script, so callers copied it per
button ([#502](https://github.com/Macro-Deck-App/Macro-Deck/issues/502)). And a widget-targeting action
inside a script had to name one specific widget, so the same script could not be reused across buttons
([#501](https://github.com/Macro-Deck-App/Macro-Deck/issues/501)).

## Decision

A script is a name, a description and exactly one action flow, stored as one JSON file per script under
`data/scripts/<id>.json` through the durable writer
([ADR 0001](0001-json-profiles-and-durable-writes.md)). The flow is kept as the raw client-serialized
string, exactly like a widget's `Data`, so the host never has to migrate a client-owned action schema.

**Execution reuses `IFlowExecutor`.** `ScriptRunner` wraps the stored flow in the envelope the executor
reads and selects `onRun` in global scope. `onRun` is not a trigger a user can pick; it is the
discriminator that lets a script reuse the widget flow shape. Conditions, loops, `$secret` resolution,
the variable snapshot and error handling are therefore identical to a widget's by construction.

**The action is an integration action.** A hidden always-active integration contributes "Run Script"
with a dynamic-choice parameter, and the SDK reaches it through `IIntegrationContext.Scripts`, so an
integration can run a script without knowing how scripts are stored.

**The action awaits the script**, because "switch scene, then start recording" is exactly the sequence
people build. The call graph is user-authored and can be cyclic, so `ScriptRunner` bounds it at a call
depth of 10 through an `AsyncLocal` rather than forbidding re-entry — a script running another script is
the point of the feature.

**The editor is the action-builder in single-flow mode**, taking a caller-supplied trigger-tab set; when
that set holds one tab the tab row disappears entirely.

**Archives carry the scripts a widget runs.** Export walks the exported widgets for script ids, expands
them transitively, and bundles them; import mints fresh ids for all of them *before* storing any, then
rewrites every bundled flow through the same old-to-new map the icons and secrets already use.

### Inputs are read-only `vars` that exist for one run

An input is read as **`vars.<name>`** — the same namespace as a persisted variable, not a third one
beside `vars.` and `event.`. For one run a declared input **shadows** a global of the same name, in both
the `{{ vars.x }}` template form and the `{"$var":"x"}` reference form, which resolve through the same
per-run map so they cannot disagree.

- **Inputs are read-only.** A `Set Variable` block targeting a name currently occupied by an input fails
  that block and names the input. Shadowing without this rule is a silent read/write asymmetry.
- **The overlay is per execution, never ambient**, so two concurrent runs see their own values and an
  input cannot leak into a nested script that did not declare it.
- **Declarations are a typed field on the script**, not part of the client-owned flow blob, because the
  host must read them to coerce values, apply defaults and enforce the read-only rule.
- **The caller is answerable for the values it supplies.** A required input with no value and no default,
  or a value that does not fit its declared type, fails the run before any block executes. Half-running a
  script that skipped the step needing the value is worse than not running it.
- **Undeclared supplied names are dropped**, so no caller can inject arbitrary `vars.*` into a script.

### The widget is the run's owner, not an input

A script carries one boolean, `RunsOnWidget`. `IScriptRunner.RunAsync` and the public
`IScriptApi.RunAsync` take an owner-widget argument directly, and `ScriptRunner` sets it on the same
`OwnerWidgetId` field a widget's own flow already populates — so `$self` inside the script resolves
through the existing widget-target machinery with no new resolution logic anywhere.

An implicit "current widget" ambient context was rejected: scripts also run from automations, the
Scripts page, plugins and HTTP, where no widget exists, so the same script would behave differently
depending on where it was invoked. An earlier design made the widget a new `ScriptInputType` member,
which is a protocol break — an already-compiled plugin's converter throws on an unrecognised wire string
— and let a script declare several widget targets, which nothing asked for.

Delegation to a remote host does not support this: a plugin has no `$self` to resolve, so no owner
widget is sent and the editor says so.

## Consequences

- A sequence is authored once and run from any number of widgets; editing the script changes every
  caller. Because there is one engine, a script gains every future execution feature with no work.
- The delete confirmation is honest: a usages request scans widget data and other scripts, so the dialog
  names the folders and counts affected. It is a request rather than a field on the script DTO because
  the answer changes whenever a widget elsewhere changes.
- A script id is a GUID inside client-owned JSON, discovered the same schema-agnostic way icon references
  are. That is why an unrelated GUID can never be mistaken for a script: the candidate must resolve
  against the script store.
- Running a script that no longer exists is a **warning, not an error** — a widget can outlive the script
  it points at. The Scripts page's own Run is the exception and answers `NotFound`.
- A script archived from another machine arrives as a **new** script, not a merge, exactly as profiles do.
- The Scripts page runs the **saved** script, never the draft, so Run is disabled while there are unsaved
  changes rather than quietly executing something else.
- The cost of the `vars` namespace is shadowing: an input named like a global hides it for that run, and
  nothing in a stored flow distinguishes the two. The editor warns when a declaration shadows an existing
  global, but a global created later shadows silently.
- `IScriptApi.RunAsync` changed in place twice rather than gaining overloads — a source and binary break
  of the public SDK, taken while no plugin depended on it. The protocol did not break either time: the
  values and the owner widget travel as additive optional fields.

## Alternatives considered

- **Store scripts in SQLite.** The payload is one opaque client-owned JSON blob, so a table buys no
  queryability and costs a migration on every schema change.
- **A script as a hidden widget.** It would inherit a folder, a position and a grid cell that every
  widget consumer would need to learn to skip.
- **Fire-and-forget "Run Script".** Cheaper, but ordering across the boundary would be undefined.
- **Leaving scripts out of portable archives.** Would export a profile whose buttons silently do nothing
  on the target machine.
- **A third `script.` namespace for inputs.** Symmetrical, but it gives up the syntax users already know
  and the variable picker they already use.

## References

- [Issue #171](https://github.com/Macro-Deck-App/Macro-Deck/issues/171),
  [Issue #501](https://github.com/Macro-Deck-App/Macro-Deck/issues/501),
  [Issue #502](https://github.com/Macro-Deck-App/Macro-Deck/issues/502)
