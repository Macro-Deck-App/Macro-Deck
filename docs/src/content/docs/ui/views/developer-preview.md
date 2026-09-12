---
title: Developer preview
description: Registering [UiPreview] scenarios so Developer Tools can render a view in states that are hard to reach live.
---

A view is often easiest to judge in the states that are hard to reach: disconnected, empty, mid-load,
failing, or holding a translation three times longer than the English. Register those states as
**previews** and Macro Deck's Developer Tools lists them, renders them through the same renderer the real
app uses, and lets you resize the surface freely while it re-lays-out.

Mark a static method with `[UiPreview]`:

```csharp
internal static class SpotifyConfigViewPreviews
{
    [UiPreview("Default")]
    public static UiElement Default() => SpotifyConfigView.Build(new MockSpotifyService());

    [UiPreview("Connected")]
    public static UiElement Connected()
        => SpotifyConfigView.Build(new MockSpotifyService { IsConnected = true, UserName = "Test User" });

    [UiPreview("Error")]
    public static UiElement Error()
        => SpotifyConfigView.Build(new MockSpotifyService { Error = "Authentication failed" });
}
```

The method must be `static`, take no parameters, and return a `UiElement`, a `UiView`, or a `UiPreview`.
It takes no parameters because a scenario builds its own mocks: nothing is injected, and nothing reaches
the real service.

Scenarios group under the view they preview. The group name is the declaring type's name with a trailing
`Previews` removed - `SpotifyConfigViewPreviews` groups under `SpotifyConfigView` - or whatever you set
explicitly:

```csharp
[UiPreview("Long text", View = "WeatherDetailsView", Profile = UiPreviewProfiles.Widget)]
```

`Profile` says which component namespace the tree is authored in, `config` or `widget`. It only decides
how the preview canvas is set up before the first tree arrives; the tree itself decides how it renders.

## Releasing what a mock owns

Opening, switching, refreshing and closing a preview each end the previous session. A scenario whose mock
starts something that has to be stopped - a timer, a subscription, a fake connection - hands it over, and
Macro Deck disposes it when the preview ends:

```csharp
[UiPreview("Streaming")]
public static UiPreview Streaming()
{
    var mock = new MockTelemetryFeed();

    return UiPreview.Of(TelemetryView.Build(mock), mock);
}
```

A scenario that only builds a tree from plain data returns the element and never mentions `UiPreview`.

## What a preview cannot affect

Previews are developer tooling and are deliberately inert in a running Macro Deck:

- Discovery reads metadata only. A scenario is never called until somebody opens it, so declaring one
  costs a running host nothing.
- A preview renders on its own `developer-preview` surface. No provider you wrote for a `config`,
  `widget`, `dialog` or `folder` surface is ever asked to serve one.
- A malformed `[UiPreview]` method - not static, taking parameters, returning the wrong type - is skipped
  and reported in Developer Tools. It never fails discovery, so one bad preview cannot take down the
  surfaces your plugin really serves.
- A scenario that throws when it is opened fails only that preview.
- Listing and opening previews require an admin session.
