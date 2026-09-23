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

A returned `UiView` belongs to the preview, which disposes it when the preview ends. Build a new one on
every call rather than returning a cached instance, or the next open gets a view that ignores every event.

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

## Iterating on a preview

Open a scenario once and leave it open while you change the code. Developer Tools keeps it on screen and brings
it back after a reload.

| You change | What updates | How |
| --- | --- | --- |
| A plugin scenario or view, under `dotnet watch` or your IDE's Hot Reload | The open preview, in place | The SDK rebuilds the scenario and sends its new tree. No restart, and the preview keeps its session. |
| A plugin change Hot Reload cannot apply, or a plain rebuild and restart | The open preview, once the plugin is back | The last tree stays on screen, dimmed, with a notice that the plugin is not connected. The preview reopens by itself when the plugin reconnects. |
| A scenario that no longer exists (renamed or removed) | A notice instead of the preview | Pick the scenario again from the list, which refreshes by itself. |
| A built-in Macro Deck view | The open preview, after the host restarts | The preview reopens once Macro Deck is back. |
| Nothing, but the same scenario is opened in a second window | The second window | The first window says the preview was closed. Refresh takes it back. Different scenarios can stay open side by side. |

The selected scenario and the canvas size are part of the Developer Tools address, so reloading the window
brings you back to the same scenario at the same size.

A component view on the canvas is laid out the way a deck tile is: each preset cell is one deck cell, so a
[`UiResponsive`](/ui/components/responsive/) switches layouts at the same sizes it does on the deck. A folder
view or a dialog in the app is laid out in CSS pixels instead, 120 of them to a cell, so check its thresholds
there too.

`macrodeck-plugin run --project <path> --watch` sets this loop up against the running Macro Deck; see
[Watching for changes](/cli/run/#watching-for-changes). `dotnet watch run` with the
[debugging launch profile](/guides/debugging/#live-reload-while-you-work) does the same from your IDE.

A reload rebuilds the scenario from its code, so what the scenario creates starts over: its mock data, and
anything you typed or selected inside the preview. Put the state you want to look at into the scenario itself.

A plugin whose first-ever scenario is added by Hot Reload does not declare a UI yet, so that scenario appears
after the next restart.

## Over the plugin protocol

`describe` lists your scenarios in `previews`. `MacroDeck.Plugin.Hosting` scans the assemblies of your
registered integrations plus the entry assembly, lazily. A `developer-preview` surface names one scenario
by id in its attributes; the SDK builds that scenario ahead of every production path and never consults
`IUiProvider`. See [Serving a view](/ui/views/sessions/#over-the-plugin-protocol).

When .NET Hot Reload updates the plugin, `MacroDeck.Plugin.Hosting` builds every open preview's scenario
again, sends the new tree as an unrequested `ui/snapshot`, and sends `state.update` for `ui` so the host
re-reads `describe` and the list picks up new or removed scenarios. A scenario that throws on rebuild faults
only its own preview. Nothing changes for a plugin that is not being hot reloaded.

## See also

- [Custom views](/ui/views/custom/) - a view with previews and tests
- [Views and surfaces](/ui/views/)
