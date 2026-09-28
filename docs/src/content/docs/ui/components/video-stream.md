---
title: Video stream
description: Shows a live video stream from a video stream provider, played from a session the reader opens, suspends and closes itself.
---

A live video stream that a [video stream provider](/features/video-streams/) offers: an OBS scene, a camera,
a capture device. The tree names the stream and nothing else. The client showing it asks Macro Deck for a
session, plays whatever transport the provider answers with, and releases it again, so you never write a
player, a reconnect loop or a placeholder yourself.

`macrodeck.video-stream`

## Example

```csharp
new UiVideoStream
{
    Key = "program",
    Stream = UiValue.Of(new UiVideoStreamReference { Provider = "com.example.obs::studio", Id = "Program" }),
    Fit = UiComponentImageFits.Cover,
    Fill = true,
    Fallback = new UiTextRun { Key = "programFallback", Text = "Update Macro Deck to see the stream" },
}
```

![A wide tile with the same landscape video twice: letterboxed on the left, filling and cropped on the right](../../../../assets/ui/video-stream.png)

The same stream with `contain` on the left and `cover` on the right.

`macrodeck.video-stream` works wherever Macro Deck UI does: in a built-in widget, in your own
[widget type](/ui/views/widget-types/), in a [folder view](/ui/views/folder-views/), in a
[modal](/ui/views/modal/) or on a [screensaver](/ui/views/screensavers/), and the same whether your plugin
runs in or out of process.

## Properties

| Property | Meaning |
|---|---|
| `Stream` | The stream to show, as a `UiVideoStreamReference`. Absent shows a placeholder. |
| `Fit` | `contain` (default) shows the whole picture, letterboxed; `cover` fills the box and crops. |
| `Size` | The extent along the parent stack's main axis. |

`Provider` is the provider's qualified id, `plugin.id::provider-id`, the one
`VideoStreamProviderRegistration.QualifiedId` returns. `Id` is the stream's id within that provider.
On the wire:

```json
{"type":"macrodeck.video-stream","properties":{"stream":{"provider":"com.example.obs::studio","id":"Program"},"fit":"cover"}}
```

A reader treats a `stream` it cannot read, such as a missing member, as absent.

## Sizing

`Size` is how much of the parent stack's main axis the view takes, not the edge of a square: the other axis
is whatever the stack gives it. Absent, the view takes no space on the main axis, so give it `Size` or
`Fill`. The picture always keeps the stream's own aspect ratio inside that box. Before the first frame arrives,
the reader reserves the width and height the provider declared for the stream, so the layout does not jump.

## What the reader takes care of

- **The session.** It opens one while the view is on screen, suspends it while the view is scrolled out of
  sight or covered by the screensaver or the lock screen, and closes it when the view goes away or the
  page is hidden.
- **States.** While the stream is connecting or reconnecting, gone, or offered in no transport this device
  plays, the reader draws its own placeholder and a short message, in the reader's language. A message the
  provider sends with its session update is shown instead of the generic one.
- **Recovery.** A session that ended because the provider restarted, the connection to Macro Deck dropped or
  the source went away is opened again on its own, with a growing delay. A stream that does not exist is
  tried again when the provider's streams change.
- **Transports.** The client offers the transports it can play and uses the first one the provider serves.
  A transport that fails to play on this device is dropped for the next attempt while another is left.
  See [What Macro Deck's clients play](/features/video-streams/#what-macro-decks-clients-play).
- **Sound.** The stream always plays muted.

The reader names the view after the stream for assistive technology.

## Older readers

A reader that predates `macrodeck.video-stream` draws the node's `Fallback`, as for any type it does not
know, so set one. The type needs no `RequiredComponentVersion`.
