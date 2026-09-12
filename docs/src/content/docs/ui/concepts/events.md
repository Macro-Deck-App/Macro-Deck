---
title: Events
description: Handling events, validating input, and the rule that a component offers only the interaction its node declares.
---

## Events

Elements can declare event handlers. Writable inputs expose their change path through the binding. Event
handlers should update state or invoke application logic; they should not rebuild the whole view manually.

Async work should honor cancellation. For option loading or provider-backed choices, keep
network/provider policy in plugin code rather than in the UI runtime.

## Validation

Use the input constraints and validation rules exposed by the DSL. Keep user-facing validation close to
the field that can fix the problem.

Hidden fields and structurally absent fields are intentionally different. Choose the model that matches
whether the value should continue to exist and be submitted.

## Interaction is offered only where it is declared

This is the framework's one rule for every interactive component: a reader sends an event only where the
node's own `Events` lists it, and a node with no handlers is drawn but cannot be touched. There is no
separate disabled property to contradict it - declaring nothing *is* disabled, and declaring only some of
a component's names is how you say the rest are not yours to receive.

**A component definition's own event list is documentation, never a gate.** A `ui.slider` can carry
`adjust` and `change` because dragging a level is a continuous interaction; a `ui.button` can carry
`press`, `long-press`, `press-start` and `press-end` because a press has phases worth telling apart
separately. Nothing about the wire format enforces that a slider only ever emits its own two names or a
button its own four - it is the reader's contract to send only what a node declared, for whichever names
that node's type supports. A reader never infers `press` from a `press-start`/`press-end` pair arriving
back to back, and never sends a name absent from `Events` just because the component type happens to
support it.

## Every event name

| name | declared by | fires |
| --- | --- | --- |
| `change` | `ui.slider`, `ui.text-field` | the value the user settled on - released the drag, left the field, or pressed Enter |
| `adjust` | `ui.slider`, `ui.text-field` | continuously while the user is still working the control - every drag step, every keystroke |
| `press` | `ui.button` | a press completed without being held past the long-press threshold |
| `long-press` | `ui.button` | the press was still held after 600ms |
| `press-start` | `ui.button` | the press began |
| `press-end` | `ui.button` | the press ended, however it ended - including a cancelled gesture or the pointer leaving the element |
| `reveal` | `ui.list` | the user scrolled to reveal a child, carrying the index of the furthest one now in view |

None of these carry a payload beyond what the table describes. See the [component reference](/ui/components/)
for the geometry and semantics each name is paired with on the component that declares it, and
[Modal views](/ui/views/modal/) for `modal.complete`, the one event a tree emits that is not a
component interaction but the answer that ends a dialog.
