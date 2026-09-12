---
title: Custom views
description: A complete "now playing" folder view, from composed components through a served surface to a headless test.
---

A "now playing" folder view, end to end: the view, the provider that serves it, previews and a test.

## Example

```csharp
public static class NowPlayingView
{
    public static UiElement Build(INowPlayingService service)
    {
        var track = service.CurrentTrack; // UiState<TrackInfo?>, owned by the service

        return new UiStack
        {
            Key = "now-playing",
            Padding = 0.06,
            Gap = 0.04,
            Children =
            [
                new UiImage { Key = "art", Source = UiValue.From(() => track.Value?.Artwork!), Size = 0.6 },
                new UiTextRun { Key = "title", Text = UiText.From(() => track.Value?.Title), Size = 0.12 },
                new UiProgressBar
                {
                    Key = "progress",
                    Value = UiValue.From(() => track.Value?.Progress!),
                    Thickness = 0.05,
                },
                new UiButton
                {
                    Key = "skip",
                    Justify = "center",
                    Events = [UiEventHandler.OnAsync(UiComponentEvents.Press, service.SkipAsync)],
                    Children = [new UiTextRun { Key = "label", Text = Strings.NowPlaying.Skip(), Size = 0.12 }],
                },
            ],
        };
    }
}
```

`Progress` is a `UiProgressReference` (for example `UiProgressReference.Advancing(positionMs, anchor,
durationMs)`): each reader advances it against its own clock, so you push no patch every second. That is
why the bar is `macrodeck.progress-bar` rather than a `ui.*` component - see
[The progress family](/ui/components/progress/) and [the UI overview](/ui/).

## Serving it on a folder surface

```csharp
public sealed class NowPlayingUiProvider : IUiProvider
{
    private readonly INowPlayingService _service;

    public NowPlayingUiProvider(INowPlayingService service) => _service = service;

    public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
        [new() { Kind = UiSurfaceKinds.Folder, SessionMode = UiSessionModes.Shared }];

    public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
    {
        if (request.Surface.Kind != UiSurfaceKinds.Folder)
        {
            return Task.FromResult<IUiSession?>(null);
        }

        var view = new UiView(request.Surface, NowPlayingView.Build(_service));
        return Task.FromResult<IUiSession?>(new ViewSession(view));
    }
}
```

`ViewSession` forwards `BuildTree`, `DrainPatches`, `Changed`, `Faulted` and `Dispatch` to the `UiView` -
see [Serving a view](/ui/views/sessions/#example) for it, the lifecycle and the limits. A folder only
opens this surface once your integration has registered the view through `IFolderViewProvider`; when you
offer more than one folder view, check `UiFolderSurfaceAttributes.ViewId` and decline the others. See
[Folder views](/ui/views/folder-views/).

## Previewing it

```csharp
internal static class NowPlayingViewPreviews
{
    [UiPreview("Playing", Profile = UiPreviewProfiles.Widget)]
    public static UiElement Playing() => NowPlayingView.Build(new MockNowPlayingService
    {
        CurrentTrack = new UiState<TrackInfo?>(new TrackInfo(
            "Nightcall",
            Artwork: null,
            UiProgressReference.Advancing(42_000, DateTimeOffset.UtcNow, durationMs: 215_000))),
    });

    [UiPreview("Between tracks", Profile = UiPreviewProfiles.Widget)]
    public static UiElement BetweenTracks() => NowPlayingView.Build(new MockNowPlayingService());
}
```

See [Developer preview](/ui/views/developer-preview/) for the rules a `[UiPreview]` method follows and how
scenarios group.

## Testing it

```csharp
[Fact]
public async Task Pressing_skip_invokes_the_service()
{
    var service = new MockNowPlayingService();
    var host = UiTestHost.Render(NowPlayingView.Build(service));

    host.ById("skip").Raise(UiComponentEvents.Press);
    await host.SettleAsync();

    Assert.True(service.SkipWasCalled);
}

[Fact]
public void Title_shows_the_current_track()
{
    var service = new MockNowPlayingService
    {
        CurrentTrack = new UiState<TrackInfo?>(new TrackInfo(
            "Nightcall", Artwork: null, UiProgressReference.Halted(0, DateTimeOffset.UtcNow, durationMs: 215_000))),
    };
    var host = UiTestHost.Render(NowPlayingView.Build(service));

    Assert.NotEmpty(host.ByText("Nightcall"));
}
```

`MacroDeck.Ui.Testing` renders a view with no browser and no running host.

| Task | `UiTestHost` / `UiTestNode` |
| --- | --- |
| Query rendered nodes | `ById`, `FindById`, `ByType`, `SingleByType`, `ByText` |
| Simulate events | `Raise(name)`, `Change(value)`, `Activate()`, `Submit()` |
| Wait for async handlers and loads | `SettleAsync()` |
| Inspect the tree and emitted patches | `Tree`, `Revision`, `Patches`, `LastPatch`, `Describe()`, `DescribePatches()` |
| Snapshot public view state | `ToCanonicalJson()` |

Test observable UI behaviour, not the internals of dependency tracking.

## See also

- [Folder views](/ui/views/folder-views/)
- [Serving a view](/ui/views/sessions/)
- [Developer preview](/ui/views/developer-preview/)
- [Testing](/features/testing/)
