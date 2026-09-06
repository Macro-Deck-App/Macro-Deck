# ADR 0010: One flow engine behind widget triggers, events and automations

Status: Accepted

## Context

A widget only ever reacted to the user touching it: its `Data.flows` array held one flow per trigger
type and an executor walked the matching flow's blocks. The one existing host-driven reaction — a toggle
button bound to a variable condition — had been built as a bespoke pipeline that understands only toggle
state.

[#170](https://github.com/Macro-Deck-App/Macro-Deck/issues/170) asked for an event system integrations
can emit into plus an "On Event" widget trigger, explicitly as a foundation with a global automations
section to follow ([#116](https://github.com/Macro-Deck-App/Macro-Deck/issues/116)). Building each on its
own pipeline would have given three host-driven reaction paths with separate semantics.

Action execution was also dishonest: it returned success-shaped responses even when an integration was
disabled, an action was missing, or an executor swallowed an error, so callers could not distinguish
completion from failure without reading logs
([#255](https://github.com/Macro-Deck-App/Macro-Deck/issues/255)).

## Decision

### One executor, selected by trigger rather than by owner

`IFlowExecutor` takes a variable scope plus a `TriggerSelector` and carries no widget identity. A widget
trigger runs in `ActionButton` scope against its own locals; a script or an automation passes
`VariableScope.Global` and its own flow source. Tap and state triggers select `ByType`, keeping the
existing de-duplication; event triggers select `ById`, because a widget carries many event triggers that
all share the `onEvent` type and selecting by type would run every one of them for a single occurrence.

### Events reuse the action-parameter schema

An event declares its parameters with the same `ActionParameter` schema actions and config-flow steps
use, shipped through the same mapper, so the trigger editor generates a typed form from metadata and
**adding an event to a provider requires no UI change**. A parallel event-parameter model would have
meant a second control catalogue, a second mapper and a second set of pickers, all drifting.

`EventDefinition` splits `ConfigurationParameters` (authored on the trigger) from `PayloadParameters`
(what an occurrence carries) and declares a delivery kind of `Push` or `Scheduled`. A single
"filterable" flag cannot model both: a cron expression is not a filter over a firehose, it is a demand
that the host *produce* occurrences.

Matching needs no provider code. A configuration parameter whose name matches a payload parameter is
compared against that payload value when set and means "any" when empty; an optional condition
expression covers anything more involved. Comparison is equality unless the trigger stored an operator
beside the value, and only numeric filters may carry one — "greater than" on a scene name is an ordinal
string compare nobody meant to ask for. The operator is stored **with the value it belongs to**, which
is what keeps the full match and the `QuickReject` pre-check from honouring different ones; a pre-check
assuming equality would drop every occurrence that is merely greater and the trigger would silently
never fire.

`$event` is an overlay on the existing variable context, not a new mechanism: `VariableContext` gained a
non-mutating `WithEvent` and the one method that resolves `$var` gained an `$event` branch. That choke
point covers every parameter type and every condition operand with no change to the block walker. The
`event` root is seeded even on runs with no occurrence behind them, because a failed render returns the
raw text and a missing root would emit the literal `{{ event.x }}` into a label.

### The subscriber lookup happens before the queue

`EventBus.Publish` consults the subscription index on the caller's thread and returns without touching
the channel when nothing is listening. This is the design's load-bearing performance decision: an idle
host already emits several variable changes a second and almost none have a trigger behind them. The
dispatcher additionally rejects an occurrence a subscription provably cannot want before creating a DI
scope, because that wasted work would otherwise hold an in-flight slot and throttle a matching trigger
beside it.

The core `Variable Changed` event carries most of the catalogue: integrations already publish their
state as variables, so one core event covers "scene changed", "recording started" and "track changed"
with no polling and no per-integration code. Its variable filter is **required**, because an unfiltered
one would fire several times a second forever. Integrations declare their own events only for what a
variable cannot express: a transient occurrence carrying a payload, or state with no variable behind it.

### An automation is one event and a flow

Stored as one JSON file per automation, with a name, description, enabled flag, one event trigger and
the actions it runs. The client writes one flow and the host reads only the first, so a hand-edited file
carrying more cannot quietly behave as several automations sharing one name and one switch. Reacting to
a second event is a second automation — which keeps a name a truthful description and the enable switch
unambiguous. Two automations that should run the same actions call one script.

The owner of a trigger is a `(kind, id)` record struct rather than a bare `Guid WidgetId`, used as a
dictionary key by the index, the dispatcher's in-flight set and the scheduler. A nullable second id or a
sentinel GUID would have left every consumer guessing which kind it held.

`enabled` gates **indexing**, not filtering afterwards. The whole reason the index exists is that
publishing costs a dictionary probe; leaving a disabled automation in it would make every occurrence of
its event pay for a queue write and a dispatch before being thrown away. The runner re-checks the flag,
because an occurrence queued a moment before the toggle would otherwise still run.

Nothing may start an automation but its event: there is no "run this automation" endpoint. A script
exists to be called; an automation exists to react. Testing one is the action-builder's own Run.

### Execution results are truthful

Every executor returns an `ActionResult` stating what happened — success, accepted, or failed with a
stable error code and an optional user-safe message. The flow engine aggregates per-action outcomes and
**never converts a recorded failure into success**. Disabled blocks are skipped; deliberately unmet
conditions are normal control flow; missing or disabled integrations and actions are failures.
Cancellation returns a partial result rather than discarding outcomes already collected.

A user-triggered run may return an accepted acknowledgement when the flow exceeds the bounded
synchronous wait; it continues in its own bounded scope and the final result is delivered to the
originating client. Integrations report success only once the provider confirms completion where
confirmation is available, and `accepted` otherwise. Unexpected provider exceptions are logged with
diagnostic context and sanitized before crossing a user-facing transport.

## Consequences

- Adding an event is: declare an `EventDefinition`, call `IEventPublisher.Publish`. No host, UI or
  transport change.
- Event triggers are host-owned. `ActionButtonTriggerType` deliberately excludes `onEvent` and the
  handler rejects it: accepting the type from a client would let any client fire every event trigger on
  a widget at once, bypassing their filters.
- Trigger configuration lives inside the widget's `Data` JSON, so there is no schema migration and
  portable archives carry it. It follows that event configuration must never store a host GUID — a
  variable filter stores the *name*, since an id would dangle silently after an import.
- An automation gains every future execution feature for free and cannot drift from a widget's on loop
  guards, error handling, `$event` resolution or snapshot timing. A duplicate arrives **disabled**,
  because two identical live automations reacting to one event is almost never what was meant.
- `{{ event.x }}` does not render in the parameter types the executor never runs Liquid over. The
  structured `{"$event": "name"}` reference works everywhere because it resolves during conversion, so
  that is the form the picker emits.
- Returning success when the requested operation did not complete is a contract violation, and new
  integrations should define meaningful failure results for expected provider states.

## Alternatives considered

- **A dedicated event-parameter metadata model.** Duplicates the control catalogue and every editor that
  renders it, for no expressive gain.
- **A second execution engine for event triggers.** The widget id was used in exactly one place, so
  generalising it cost one commit — and two engines would have drifted in exactly the semantics that
  must not differ.
- **Native per-integration event providers for state changes.** A third polling loop beside the existing
  ones, where the core `Variable Changed` event already covers them.
- **Hand-rolling cron.** DST transitions and a local time that does not exist are easy to get subtly
  wrong; `Cronos` is worth the dependency.
- **An automation owning several triggers.** Built first, then withdrawn: it made one name and one
  enable switch cover several unrelated reactions, and bought nothing two automations plus a shared
  script do not.
- **An automation as a hidden widget.** It would inherit the executor and the editor, and also a folder,
  a position and a grid cell that every widget consumer would have to learn to skip.

## References

- [Issue #170](https://github.com/Macro-Deck-App/Macro-Deck/issues/170),
  [Issue #116](https://github.com/Macro-Deck-App/Macro-Deck/issues/116),
  [Issue #255](https://github.com/Macro-Deck-App/Macro-Deck/issues/255)
