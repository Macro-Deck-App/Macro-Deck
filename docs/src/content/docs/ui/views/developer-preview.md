---
title: Developer preview
description: Registering [UiPreview] scenarios so Developer Tools can render a view in states that are hard to reach live.
---

Mark static methods with `[UiPreview]` and Developer Tools renders your view in states that are hard to
reach live.

## Example

```csharp
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Previews;

internal static class WeatherViewPreviews
{
    [UiPreview("Sunny", Profile = UiPreviewProfiles.Widget)]
    public static UiElement Sunny() => WeatherView.Build(new MockWeatherService { City = "Vienna" });

    [UiPreview("Offline", Profile = UiPreviewProfiles.Widget)]
    public static UiElement Offline() => WeatherView.Build(new MockWeatherService { Offline = true });

    [UiPreview("Long city name", Profile = UiPreviewProfiles.Widget)]
    public static UiElement LongName()
        => WeatherView.Build(new MockWeatherService { City = "Llanfairpwllgwyngyllgogerychwyrndrobwllllantysiliogogogoch" });
}
```

Developer Tools lists the three scenarios under `WeatherView`, renders each through the same renderer the
app uses, and lets you resize the surface freely while it re-lays-out. Good candidates: disconnected,
empty, mid-load, failing, and a translation three times longer than the English.

## Writing a scenario

```csharp
[UiPreview("Offline")]
public static UiElement Offline() => WeatherView.Build(new MockWeatherService { Offline = true });
```

The method must be `static`, take no parameters, and return a `UiElement`, a `UiView` or a `UiPreview`.
Nothing is injected: a scenario builds its own mocks, and nothing reaches the real service.

## Grouping and profile

```csharp
[UiPreview("Long text", View = "WeatherDetailsView", Profile = UiPreviewProfiles.Config)]
```

| Member | Default | Meaning |
| --- | --- | --- |
| `Scenario` (constructor) | - | The scenario's name. Required, not blank. |
| `View` | The declaring type's name without a trailing `Previews` (`WeatherViewPreviews` becomes `WeatherView`) | The view the scenario groups under. |
| `Profile` | `UiPreviewProfiles.Config` (`config`) | The component namespace the tree is authored in: `config` or `widget`. It only sets up the canvas before the first tree arrives; the tree decides how it renders. |

## Releasing what a mock owns

```csharp
[UiPreview("Live feed", Profile = UiPreviewProfiles.Widget)]
public static UiPreview LiveFeed()
{
    var feed = new MockWeatherFeed();

    return UiPreview.Of(WeatherView.Build(feed), feed);
}
```

Opening, switching, refreshing and closing a preview each end the previous session. Hand anything that
must be stopped - a timer, a subscription, a fake connection - to `UiPreview.Of` as an `IDisposable` or
`IAsyncDisposable`, and Macro Deck disposes it when the preview ends. A scenario that only builds a tree
from plain data returns the element and never mentions `UiPreview`.

## What a preview cannot affect

| Fact | Consequence |
| --- | --- |
| Discovery reads metadata only. | A scenario runs only when someone opens it; declaring one costs a running host nothing. |
| A preview renders on its own `developer-preview` surface. | No provider you wrote for `config`, `widget`, `dialog` or `folder` is ever asked to serve one. |
| A malformed `[UiPreview]` method (not static, takes parameters, wrong return type) is skipped and reported in Developer Tools. | Discovery never fails, so one bad preview cannot take down your real surfaces. |
| A scenario that throws when opened fails only that preview. | Other previews and sessions carry on. |
| Listing and opening previews require an admin session. | Previews are not reachable by deck clients. |

## Over the plugin protocol

`describe` lists your scenarios in `previews`. `MacroDeck.Plugin.Hosting` scans the assemblies of your
registered integrations plus the entry assembly, lazily. A `developer-preview` surface names one scenario
by id in its attributes; the SDK builds that scenario ahead of every production path and never consults
`IUiProvider`. See [Serving a view](/ui/views/sessions/#over-the-plugin-protocol).

## See also

- [Custom views](/ui/views/custom/) - a view with previews and tests
- [Views and surfaces](/ui/views/)
