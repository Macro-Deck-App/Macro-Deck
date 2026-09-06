---
title: Patches
description: How a tree changes without a full resend, and the revision rule that keeps a patch attributable to the state it was computed from.
---

A patch is a bounded sequence of operations against one tree, applied atomically. Applying it advances
the tree's **revision** by exactly one - never more, never less, and never on a patch that carries no
operations. A provider that computed a patch against an earlier revision than the session currently holds
gets refused and resynced with a full tree rather than having the patch silently misapplied; see
[what a refused update looks like](/sdk/ui/views/sessions/#what-a-refused-update-looks-like) for the
complete set of ways a patch or a tree can be rejected.

The operation vocabulary itself - what an operation targets, what it carries, and which shapes are valid
against which node kinds - is defined in
[`ui-model/src/MacroDeck.Ui.Model/Patches/`](https://github.com/Macro-Deck-App/Macro-Deck/tree/main/ui-model/src/MacroDeck.Ui.Model/Patches)
rather than restated here; treat that source as the operation reference, and this page as the rule that
governs how any of them apply.

In practice you rarely construct a patch by hand: `MacroDeck.Ui`'s reactive runtime computes and emits
the minimal patch for you from a state change - see [Reactive updates](/sdk/ui/concepts/reactive-updates/).
Reach for the operations directly only when you are serving a tree straight from `MacroDeck.Ui.Model`,
without the DSL.
