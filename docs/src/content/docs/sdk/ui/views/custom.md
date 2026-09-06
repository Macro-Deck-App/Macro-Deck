---
title: Custom views
description: A complete "now playing" folder view, from composed components through a served surface to a headless test.
---

Nothing about `MacroDeck.Ui` is specific to configuration, deck widgets or modals - a
[folder view](/sdk/folder-views/) is an ordinary `IUiProvider` serving an ordinary tree, and this page
walks one all the way from composition to a passing test.

## The view

A "now playing" card: artwork, a title, a progress bar that keeps advancing on its own, and a skip
button.

```csharp
public static class NowPlayingView
{
    public static UiElement Build(INowPlayingService service)
    {
        var track = service.CurrentTrack; // UiState<TrackInfo>, owned by the service

        return new UiStack
        {
            Key = "now-playing",
            Padding = 0.06,
            Gap = 0.04,
            Children =
            [
                new UiImage
                {
                    Key = "art",
                    Source = UiValue.From(() => track.Value.ArtworkHandle),
                    Size = 0.6,
                },
                new UiTextRun
                {
                    Key = "title",
                    Text = UiValue.From(() => UiText.Of(track.Value.Title)),
                    MainSize = UiSize.FromBasis(0.9),
                },
                new UiProgressBar
                {
                    Key = "progress",
                    Value = UiValue.From(() => track.Value.Progress),
                    Thickness = 0.05,
                },
                new UiButton
                {
                    Key = "skip",
                    Justify = UiJustify.Center,
                    Events = [UiEventHandler.On(UiComponentEvents.Press, () => service.SkipAsync())],
                    Children = [new UiTextRun { Key = "label", Text = UiText.Of("Skip"), Size = 0.12 }],
                },
            ],
        };
    }
}
```

`macrodeck.progress-bar` is `macrodeck.*` rather than `ui.*` because `track.Value.Progress` is a
`UiProgressReference` - the reader resolves it against its own clock rather than us pushing a patch every
second. See [The progress family](/sdk/ui/components/progress/) and
[the `ui.*`/`macrodeck.*` rule](/sdk/ui/) for why.

## Serving it on a folder surface

```csharp
public sealed class NowPlayingFolderView : IUiProvider
{
    private readonly INowPlayingService _service;

    public NowPlayingFolderView(INowPlayingService service) => _service = service;

    public IReadOnlyList<UiSurfaceDeclaration> Surfaces =>
        [new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Folder, SessionMode = UiSessionModes.Shared }];

    public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
    {
        if (request.Surface.Kind != UiSurfaceKinds.Folder)
        {
            return Task.FromResult<IUiSession?>(null);
        }

        var view = new UiView(NowPlayingView.Build(_service));
        return Task.FromResult<IUiSession?>(new NowPlayingSession(view));
    }
}
```

`NowPlayingSession` forwards `BuildTree`, `DrainPatches`, `Changed`, `Faulted` and `Dispatch` to the
`UiView` it wraps - see [Serving a view](/sdk/ui/views/sessions/) for what each of those means and the
limits the session is held to. Register `NowPlayingFolderView` the way any `IFolderViewProvider`'s tree is
served; see [Folder views](/sdk/folder-views/) for the registration side.

## Previewing it

```csharp
internal static class NowPlayingViewPreviews
{
    [UiPreview("Playing")]
    public static UiElement Playing() => NowPlayingView.Build(new MockNowPlayingService
    {
        CurrentTrack = MockTrack.Playing("Nightcall", elapsedMs: 42_000, durationMs: 215_000),
    });

    [UiPreview("Between tracks")]
    public static UiElement BetweenTracks() => NowPlayingView.Build(new MockNowPlayingService
    {
        CurrentTrack = MockTrack.Empty(),
    });
}
```

See [Developer preview](/sdk/ui/views/developer-preview/) for what governs a `[UiPreview]` method and how
scenarios group under the view they preview.

## Testing it

`MacroDeck.Ui.Testing` renders the view without a browser or running host. Use it to:

- query rendered elements;
- simulate events;
- update state and inspect the resulting tree;
- verify emitted patches apply cleanly;
- snapshot meaningful public view state when a snapshot is the clearest contract.

```csharp
[Fact]
public async Task Pressing_skip_invokes_the_service()
{
    var service = new MockNowPlayingService
    {
        CurrentTrack = MockTrack.Playing("Nightcall", elapsedMs: 42_000, durationMs: 215_000),
    };
    using var host = UiTestHost.Render(NowPlayingView.Build(service));

    await host.Find("skip").PressAsync();

    Assert.True(service.SkipWasCalled);
}

[Fact]
public void Title_reflects_the_current_track()
{
    var service = new MockNowPlayingService
    {
        CurrentTrack = MockTrack.Playing("Nightcall", elapsedMs: 0, durationMs: 215_000),
    };
    using var host = UiTestHost.Render(NowPlayingView.Build(service));

    Assert.Equal("Nightcall", host.Find("title").Text());
}
```

Test observable UI behavior, not internal dependency-tracking implementation.
