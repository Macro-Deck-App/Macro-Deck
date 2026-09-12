---
title: Modal views
description: Opening a dialog from an action, how it is sized, which node completes it, and what bounds the wait.
---

An action can put a dialog on the client that ran it, and - when it needs one - wait for the answer.

```csharp
public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
{
    if (context.Ui is null)
    {
        // Backend-initiated: nobody to ask. Not a failure - decide what your action does without one.
        return ActionResult.Success();
    }

    var result = await context.Ui.ShowModalAsync<DevicePick>(context.OriginClientId,
        new ModalDefinition { ViewId = "device-picker", Title = MyStrings.SelectDevice() },
        context.CancellationToken);

    if (result.Cancelled)
    {
        return ActionResult.Success();
    }

    await TransferAsync(result.Value!.DeviceId);
    return ActionResult.Success();
}
```

`ModalDefinition.ViewId` names one of your dialogs; the tree is built by your own `IUiProvider` when
Macro Deck opens a `dialog` surface, exactly as a deck widget's is. A modal that shows something rather
than asking something uses the non-generic `ShowModalAsync`, which returns as soon as the modal is open.

`UiDialogSurfaceAttributes` carries `modalId`, `viewId` and whatever `Data` the action passed. Decline a
`viewId` you do not serve rather than guessing.

A dialog is `exclusive`: it belongs to one client, so unlike a deck widget its tree may carry entered
values.

## How a dialog is sized

A dialog is sized the same way every view is: its lengths are fractions of the box Macro Deck hands it -
see [Sizing](/ui/concepts/sizing/). That box is a fixed size rather than one that grows to your tree:
sizing derives every length from the box, so a box that grew to its content would feed that content's own
height back into the basis it was measured against. A tree taller than its box scrolls vertically. It
never scrolls sideways - a tree wider than its box is an authoring mistake, and Macro Deck clips it rather
than hiding it behind a scrollbar.

## Completing a modal

The tree finishes the modal by emitting the `modal.complete` node event; its payload becomes the value
the action receives. Anything else the tree emits is an ordinary event routed back to your session. Any
node can be the one that raises it - typically a `ui.button` whose `press` handler dispatches
`modal.complete` with the picked value as its payload, such as a row in a `ui.list` of choices.

![A device picker dialog: a list of three button rows, each with a device icon, a name and a grey status line](../../../../assets/ui/view-modal.png)

## What bounds the wait

`Cancelled` is the distinction to check before touching `Value`: a user who dismissed the dialog decided
nothing. **Everything that is not an explicit completion is a cancellation** - the user dismissing it, the
client disconnecting, the flow being cancelled, the session faulting, and the run reaching Macro Deck's
maximum flow duration. An action therefore never has to tell "cancelled" from "never answered", and
awaiting a modal cannot hang.

A flow that has not finished within a few seconds detaches and keeps running, which is what makes awaiting
a person viable at all. It is not unbounded: a running flow occupies one of the host's concurrent run
slots for as long as its modal is open, and a client may only be shown a few modals at once. Do not hold a
modal open as a substitute for a deck widget.
