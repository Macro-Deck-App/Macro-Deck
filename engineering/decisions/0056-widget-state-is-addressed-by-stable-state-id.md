# ADR 0056: Widget state is addressed by stable state id, and the widget host API is versioned per protocol major

Status: Accepted

## Context

An Action Button's state was a boolean: widget data carried a mode, a persisted toggle flag and an
optional binding condition, the host derived a `bool`, mirrored it onto a variable and pushed it to
clients. Every appearance question was answered off that boolean, and the public SDK encoded it directly
in a four-member state selector and a "has on/off states" flag.

[#612](https://github.com/Macro-Deck-App/Macro-Deck/issues/612) requires a button to hold any number of
states, and requires an *action* to be able to supply them — a play/pause action offering Playing, Paused
and Stopped, a Teams toggle offering Muted, Unmuted and Unavailable. Neither fits a boolean, and a user
should not have to discover an integration's variable name to build a stateful button.

Two constraints shape the answer. Appearance is authored per state and must survive the user renaming
that state, so a state needs an identity that is not its label. And a plugin compiled against the current
SDK must keep working, while the widget appearance API is frozen public surface whose whole vocabulary
assumes exactly two appearances.

## Decision

**A widget's states are an ordered set of stably-identified entries, and every API addresses a state by
its id.** A state's id is assigned once and never derived from its label; the label is display text the
user may change at any time without losing what they configured.

### One authority decides the current state

A button's current state comes from exactly one of three sources, in this precedence:

1. a **state provider** — one configured action instance supplying both the state set and the active state;
2. a **state mapping** — ordered condition rules, first match wins, with exactly one fallback;
3. an **explicit** value set by the Set or Toggle actions.

Provider and mapping are mutually exclusive and both authoritative. **A provider that cannot answer holds
its last known state and never falls through to the mapping**: falling through would silently change what
a button means whenever an integration restarted, which is worse than showing a stale state. Only the
explicit value is persisted — a derived value has an authority it can re-ask at boot, an explicit one does
not. Providers declare their own "cannot tell" state alongside the real ones, so a user can style a
disconnected button differently from a connected one that is merely off.

### A provider is a configured instance, not an action type

The state read answers from the parameters its instance was configured with, so the same action can sit on
many buttons with different configuration and each answers for itself. Which instance is a given button's
provider is the host's decision and is never asserted by the action.

**The read is polled, not pushed.** A push cannot express this: the capability update signal is keyed by
declared capability id — the action *type* — and a configured instance has no wire identity, so a plugin
cannot name the instance whose state changed. A capability update therefore triggers a coalesced early
re-read, and the poll remains the correctness floor.

An action execution may name an advertised expected state id on a successful result. For that configured
instance only, the host may show the expected id for a few seconds while stale reads disagree; a matching
read confirms it and restores provider authority immediately, and an expired or failed expectation has no
authority. This bounded exception keeps a press responsive without changing long-term ownership.

### A press advances state only where no authority answers

A press does not change state implicitly when a provider or a mapping is configured — state changes
through an authority or through an explicit action. Where a state-mode button has **neither**, a short
press advances it to its next state, wrapping from the last back to the first
([#718](https://github.com/Macro-Deck-App/Macro-Deck/issues/718)); the advance runs before the press's own
flow is dispatched, so the flow and any state-change handler observe the state the press switched to. Long
press, touch start and touch end are unaffected.

This does not reopen the precedence: **a press must never override an authority**. The advance is a plain
call to the same guarded write path the explicit actions already use, so a configured provider or mapping
refuses it exactly as before and the press's own flow still runs. What changed is only that a button with
*no* authority no longer requires an explicit action to step through its states.

One press deliberately does not advance: a short press whose flow is a local deck navigation, which the
client resolves without calling the host at all. Routing it through the host purely to advance would run
the navigation twice, and paying that to step a state the user is navigating away from is not worth it.

### The break is the widget host API, and only that

The state-provider capability is **additive within the existing protocol major**: one descriptor field and
one operation, so a plugin speaking that major can be a state provider.

What genuinely breaks is the `widgets` host API, and it breaks **semantically rather than syntactically**.
The payload stays parseable — the selector travels as an integer and unknown fields are skipped — but what
"on", "off" and "both" denote on an N-state button, and what "has on/off states" answers for a three-state
one, are different questions than the old contract asked. The compatibility policy counts changed
observable behaviour as breaking even when the spelling is untouched, so this took a protocol major with
the previous one still served.

The WebSocket sub-protocol string names the protocol *family*, not the major; encoding the major there
would create a second version-negotiation channel that can disagree with the session handshake.

**The old major is emulated, not merely tolerated.** For as long as it is negotiable, an old plugin's
appearance request maps: "current" to the active state; "off"/"on" to the states literally id'd `off`/`on`,
failing that the first and second, failing that a no-op with a log; "both" to every state including a
third and beyond; and "has on/off states" to whether the widget has more than one. States past the first
two are invisible and unreachable to an old plugin, but nothing it does fails — and the default state ids
are literally `off` and `on` precisely so this mapping lands on real states for every upgraded button.
Translation lives in one seam keyed on the session's negotiated version, not per capability, because the
widget API is a host API that per-capability negotiation cannot reach.

## Consequences

- Stored widget data upgrades **on read** and converges on the next save. There is no one-shot migration,
  because portable archives can reintroduce old data long after release, so read-side normalization is
  required regardless.
- The old selector, its request field and the on/off flag are deprecated with a declared removal rather
  than removed now — the deprecation lifecycle's first real use
  ([ADR 0037](0037-sdk-deprecation-is-declared-metadata.md)).
- Two user-visible breaks landed deliberately: the toggle-mirroring variable no longer exists, and a press
  no longer toggles state where an authority is configured. Existing profiles keep loading; buttons
  relying on either behave differently until their owner updates them. A stored binding condition is *not*
  in that category — it is translated to an equivalent one-rule mapping.
- Serving two protocol majors means every host-API payload shape is now potentially version-shaped. Only
  one is today; adding a second should reuse the same seam rather than growing another.
- A provider read costs a poll per button that follows one. Providers declare an interval, the host clamps
  it, and a button nothing is displaying is read less often.
- A persistence failure during an implicit advance is logged rather than surfaced: a press must not fail
  because its state write did. Because the advance is awaited first, a slow state-change flow now delays
  the press flow behind it.

## References

- [Issue #612](https://github.com/Macro-Deck-App/Macro-Deck/issues/612),
  [Issue #718](https://github.com/Macro-Deck-App/Macro-Deck/issues/718)
- [ADR 0026](0026-plugin-protocol-and-sdk-boundary.md) — protocol versioning.
