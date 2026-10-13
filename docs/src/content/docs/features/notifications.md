---
title: Notifications
description: Show toasts and dialogs on every connected Macro Deck client, keep an entry in the notification list, and get the user's answer back with INotificationService.
---

A plugin shows a short message, or asks a question, on the screens Macro Deck is running on: the desktop
app, the web client and the Companion app. `IIntegrationContext.NotificationService` is the one API for all of
it. The older `IIntegrationContext.Notifications` (`IUserNotifier`) still works and only adds an entry to the
notification list.

A message for the person at the computer itself, shown by the operating system as a Windows toast, a macOS
notification or a Linux desktop notification, goes through `IIntegrationContext.DesktopNotifications`
instead; see [Desktop notifications](#desktop-notifications).

| To | Use |
| --- | --- |
| Show a toast or dialog on the Macro Deck clients, or ask and wait for an answer | `NotificationService` (`INotificationService`) |
| Leave an entry in Macro Deck's notification list | `NotificationService` with `Inbox`, or the older `Notifications` (`IUserNotifier`) |
| Show a notification from the computer's operating system | `DesktopNotifications` (`IDesktopNotifier`) |

## Example

```csharp
public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
{
    var result = await _context.NotificationService.ShowForResultAsync(
        new NotificationOptions
        {
            Title = MyStrings.DeleteRecordingTitle(),
            Message = MyStrings.DeleteRecordingMessage(),
            Severity = NotificationSeverity.Warning,
            Popup = NotificationPopup.Dialog,
            Actions =
            [
                new NotificationAction { Id = "delete", Label = MyStrings.Delete(), Style = NotificationActionStyle.Danger },
                new NotificationAction { Id = "keep", Label = MyStrings.Keep() },
            ],
        },
        context.CancellationToken);

    return result.ActionId == "delete" ? await DeleteAsync() : ActionResult.Success();
}
```

The first valid answer from any targeted client wins. Macro Deck closes the popup on every other client and
`ShowForResultAsync` returns once, with the id of the pressed action and who pressed it.

## What a notification is

| Member | Meaning |
| --- | --- |
| `Title`, `Message` | `LocalizedText`. Every client resolves it in its own language. The title is required, at most 200 characters for literal text; the message at most 1000. |
| `Severity` | `Info`, `Success`, `Warning`, `Error`. |
| `Popup` | `None`, `Toast` (the default) or `Dialog`. A notification has at most one popup, so a toast and a dialog cannot be combined. |
| `Inbox` | Also keeps an entry in the notification list. It is independent of the popup: closing the popup never removes the entry, and dismissing the entry never closes the popup. At least one of `Popup` and `Inbox` must be set. A notification with `Popup = None` and no `Progress` is only an entry: it is complete as soon as it is added, with `Programmatic` as its reason, and cannot be updated. |
| `Duration` | How long the popup stays. A toast defaults to 6 seconds. A dialog stays until answered. Macro Deck clamps it to between 1 second and one hour and closes every popup after one hour at the latest, with the reason `Timeout`, also a dialog you are waiting on. |
| `Dismissible` | Whether the user can close the popup without choosing an action. A dialog needs an action, `Dismissible`, or a `Duration`. |
| `Target` | `NotificationTarget.All` (the default), `NotificationTarget.AdminUi` (the desktop app and administrator sessions) or `NotificationTarget.Devices(...)`. Devices are named by their device id, which Macro Deck takes from the authenticated connection. |
| `Actions` | Up to four buttons. Only the declared ids are accepted as a response. |
| `Content` | A static Macro Deck UI tree for a dialog, see [Forms](#forms). |
| `Progress` | A progress bar, determinate with a `Total` or indeterminate without. Change it with `UpdateAsync`. |

Macro Deck decides where a notification comes from. Every client shows the source (Macro Deck, an integration
or a plugin, plus its name) and a plugin cannot present itself as anything else.

## Updating and dismissing

`ShowAsync` returns an `INotificationHandle` as soon as the notification is open.

```csharp
var handle = await context.NotificationService.ShowAsync(
    new NotificationOptions { Title = "Exporting", Inbox = true, Progress = new NotificationProgress(0, 40) });

for (var done = 1; done <= 40; done++)
{
    await ExportNextAsync();
    await handle.UpdateAsync(new NotificationUpdate { Progress = new NotificationProgress(done, 40) });
}

await handle.UpdateAsync(new NotificationUpdate { Title = "Export finished", Severity = NotificationSeverity.Success });
await handle.DismissAsync();
```

`NotificationUpdate` changes the title, message, severity, progress, actions or content. A member left `null`
keeps its value. Every update restarts the duration of a toast, so a toast that reports progress stays up while
it keeps being updated; the maximum lifetime still ends it. The popup kind, target and duration are fixed when it is shown. Only the integration that
showed a notification can update or dismiss it. `handle.Completion` completes exactly once, whatever closed
the notification.

## How it ends

`NotificationResult.CloseReason`:

| Reason | When |
| --- | --- |
| `Action` | A client pressed one of the actions. `ActionId`, `ClientId` and `Values` are set. |
| `Dismissed` | A client closed the popup without choosing an action. |
| `Timeout` | The duration, or the maximum lifetime, elapsed. |
| `Programmatic` | The owner called `DismissAsync`. |
| `OwnerGone` | The owner went away, for example the plugin was unloaded. |
| `HostUnavailable` | The host restarted or could not be reached, so the outcome is unknown. Notifications live in memory and do not survive a host restart. |

`ClientId` is the device id of the responding client, or the literal `admin` for the desktop app and
administrator sessions, which have no device id.

Cancelling the token of `ShowForResultAsync` dismisses the notification and throws
`OperationCanceledException`.

## Forms

`Content` lets a dialog ask for values. It is a Macro Deck UI tree in the widget profile that Macro Deck
renders as it renders any other tree, with no session and no event round trip: the tree is a snapshot.

A `ui.text-field`, `ui.toggle`, `ui.slider` or `ui.segmented` node that declares a `change` or `adjust` event
is an input and its node id is the field name. `NotificationResult.Values` contains every input of the tree:
what the user entered, or the value the tree started with when they did not touch it.

| Component | Value | Accepted |
| --- | --- | --- |
| `ui.text-field` | JSON string | At most 1000 characters |
| `ui.toggle` | JSON boolean | |
| `ui.slider` | JSON number | 0 to 1 |
| `ui.segmented` | JSON integer | 0 up to the number of segments minus one |

Macro Deck rejects a response with an unknown field or a value outside these limits, tells the client, and
keeps the dialog open. The widget profile has no `required` flag or pattern: check the returned values
yourself and ask again with another notification if they are not good enough.

```csharp
var result = await context.NotificationService.ShowForResultAsync(new NotificationOptions
{
    Title = "Name this scene",
    Popup = NotificationPopup.Dialog,
    Actions = [new NotificationAction { Id = "save", Label = "Save", Style = NotificationActionStyle.Primary }],
    Content = Form.Build(), // a UiNode with a text field whose Id is "name"
});

var name = result.Values["name"].GetString();
```

A dialog tree may hold up to 200 nodes, nested at most 12 levels, and 32 KiB in all.

## Limits

| Limit | Value |
| --- | --- |
| Open notifications per plugin | 20 |
| Open notifications in total | 50 |
| Content of all open notifications | 64 KiB per plugin, 256 KiB in total |
| Notifications shown per minute | 30 per plugin |
| Updates per second | 20 per plugin |

A call over a limit throws `NotificationException` with `NotificationErrorCode.LimitExceeded`. Macro Deck
sends updates to the clients at most four times a second per notification, so a burst of progress updates
costs nothing on the screens.

## Reconnecting clients

A client that connects while a popup is open gets it, and a client that was away learns what closed in the
meantime: Macro Deck sends the list of open popups to every client when it connects. A queued popup that the
host closed or that timed out disappears even if it was never shown.

## Notification list

The desktop app shows the list. Entries are unread until the user reads them, and the list keeps the
newest 100, dropping the oldest read entry first. The list stays on the host's machine and is not kept over
a restart. Entries are written in the host's language, while a popup is shown in each client's own. Only
the desktop app and administrator sessions see the list, also when the popup was sent to a device.

## From an action

Users get the same thing without code: the System integration has the action **Show Macro Deck
notification** with title, message, severity, how to show it and a timeout, and it renders template
variables like any other action parameter. See [Notifications](/guide/concepts/#notifications) in the user
guide.

## Over the plugin protocol

A plugin that uses the SDK needs nothing else. On the wire, `host.invoke` against the `notifications` api
has the operations `show`, `update` and `close` next to the older `notify` and `dismiss`. `show` answers
with the notification id as soon as it is open, never when somebody answers, because a person is not bound
by the request deadline. How the notification ended arrives later as a `ui` / `notification.result`
`capability.invoke`, exactly once per notification. A result the plugin cannot take while it is detached is
kept and sent when it reattaches. A host that predates these operations answers `CAPABILITY_UNSUPPORTED`, which
the SDK reports as `NotificationErrorCode.Unsupported`; a plugin written before them keeps working
unchanged. See the [WebSocket reference](/reference/websocket/).

## Desktop notifications

```csharp
if (_context.DesktopNotifications.IsSupported)
{
    await _context.DesktopNotifications.ShowAsync(MyStrings.RecordingStopped(), MyStrings.SavedTo(path), cancellationToken);
}
```

`IDesktopNotifier.ShowAsync` hands a title and a message to the operating system of the computer Macro Deck
runs on. Macro Deck shows it through its own app first, so the notification carries Macro Deck's name and
icon, and falls back to the operating system's own tool when the app is not running. The System integration's
**Send notification** action uses the same path.

- **Best effort.** The call never throws because a notification could not be shown: while `IsSupported` is
  `false`, or when Macro Deck refuses or fails, it completes without showing anything. The operating system
  can still hide it, for example under a focus mode, and nothing is reported back. Only cancellation throws.
- **`IsSupported`** is `false` on a computer without desktop notifications, such as Linux without
  `notify-send`, and for a plugin that may not show them.
- **Plugins declare `host:desktop-notifications`.** An installed plugin without it cannot show desktop
  notifications; a session registered with a developer token or pairing counts as declaring it. An
  integration built into Macro Deck always may. See [the manifest](/reference/manifest/) and
  [the security model](/policies/security/#desktop-notifications).
- **Older Macro Deck versions.** A plugin learns `IsSupported` when it registers. Macro Deck before
  desktop notifications reports nothing, so `IsSupported` stays `false` and `ShowAsync` shows nothing. An
  `IIntegrationContext` implemented against an older SDK inherits the same behaviour from
  `UnsupportedDesktopNotifier`.

On the wire this is the [`desktop-notifications`](/reference/websocket/#desktop-notifications) host API. In
`MacroDeck.Plugin.Testing`, `FakeIntegrationContext.DesktopNotifications` is a `FakeDesktopNotifier` that
records what it showed in `Shown`; set `IsSupported` to `false` to test the other case.

## Testing

`FakeIntegrationContext.NotificationService` in `MacroDeck.Plugin.Testing` is a `FakeNotificationService`:
it records what a plugin showed and a test answers it with `Respond` or `Close`.

```csharp
var context = new FakeIntegrationContext();
var asking = plugin.AskAsync(context);

context.NotificationService.Open.Single().Respond("delete");
await asking;
```
