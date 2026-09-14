---
title: Screensavers
description: Offering what a device shows after it has sat idle, and what Macro Deck does with the touch that wakes it.
---

A screensaver is a Macro Deck UI tree a device shows in place of its deck after it has sat idle for the
time its settings name - a clock, the current track, a photo frame. The first touch brings the deck back.

## Example

```csharp
public sealed class PhotoIntegration : IPluginIntegration, IScreenSaverProvider, IUiProvider
{
    private string? _photos;

    public string ProviderName => "Photo frame";

    public async Task InitializeAsync(IScreenSaverProviderContext context, CancellationToken cancellationToken = default)
    {
        var registration = await context.RegisterScreenSaverAsync(
            new ScreenSaverDescriptor(
                "photos",
                MyStrings.PhotosName(),
                MyStrings.PhotosDescription(),
                HasConfiguration: true),
            cancellationToken);

        _photos = registration.ScreenSaverId; // "com.example.photos::photos"
    }

    public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
    [
        new UiSurfaceDeclaration { Kind = UiSurfaceKinds.ScreenSaver, SessionMode = UiSessionModes.Shared },
        new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
    ];

    public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
    {
        var surface = request.Surface;
        var attributes = surface.Attributes;

        UiElement? root = surface.Kind switch
        {
            UiSurfaceKinds.ScreenSaver
                when attributes[UiScreenSaverSurfaceAttributes.ScreenSaverId].GetString() == _photos
                => Photos(attributes[UiScreenSaverSurfaceAttributes.Configuration]),
            UiSurfaceKinds.Config
                when attributes[UiConfigSurfaceAttributes.EntryPoint].GetString() == UiConfigEntryPoints.ScreenSaverConfig
                && attributes[UiConfigSurfaceAttributes.ScreenSaverId].GetString() == _photos
                => PhotosConfig(attributes[UiConfigSurfaceAttributes.ScreenSaverConfiguration]),
            _ => null,
        };

        return Task.FromResult<IUiSession?>(root is null ? null : new ViewSession(new UiView(surface, root)));
    }

    private UiStack Photos(JsonElement configuration)
    {
        var album = configuration.TryGetProperty("album", out var value) ? value.GetString() : null;

        return new UiStack
        {
            Key = "photos",
            Justify = UiComponentJustify.Center,
            Align = UiComponentAlignments.Center,
            Children = [new UiImage { Key = "photo", Source = UiValue.Of(NextPhoto(album)), Size = 0.9 }],
        };
    }

    // IPluginIntegration members, NextPhoto and PhotosConfig omitted.
}
```

`ViewSession` is the adapter from [Serving a view](/ui/views/sessions/#example). The shape is
[folder views](/ui/views/folder-views/) one level up: a provider registers descriptors to be listed in a
device's settings, and serves the screensaver through its `IUiProvider` when a device opens the surface.
Macro Deck ships a clock and a now-playing screensaver through the very same contract.

## Registering a screensaver

```csharp
var registration = await context.RegisterScreenSaverAsync(descriptor, cancellationToken);
// registration.ScreenSaverId == "com.example.photos::photos"
```

The qualified id is what a device stores. **Keep it stable across releases** - renaming it strands every
device already using the screensaver. Registration is a push: register when you are ready, withdraw when
you are not. `GetScreenSavers()` only lets Macro Deck recover its catalog after a reconnect, and is
optional. There is no `ShutdownAsync` here: release what `InitializeAsync` acquired in your integration's
own `ShutdownAsync`, and Macro Deck withdraws your screensavers itself.

Against a host older than this capability the registration answers with an empty `ScreenSaverId` and
nothing is offered; the call does not throw, so the rest of your integration starts as usual.

## Drawing the screensaver

```csharp
var screenSaverId = attributes[UiScreenSaverSurfaceAttributes.ScreenSaverId].GetString();
var configuration = attributes[UiScreenSaverSurfaceAttributes.Configuration];
```

A `screensaver` surface carries `deviceId`, `screenSaverId` and `configuration`. The configuration travels
with the request because you cannot read Macro Deck's stored devices. Decline a screensaver you do not
serve rather than guessing from the configuration's shape. The tree fills the whole display; lengths are
fractions of the display's smaller side, so a full-screen clock is drawn with the same numbers as a
widget-sized one - see [Sizing](/ui/concepts/sizing/).

**Movement has to be cheap.** A screensaver often runs on a tablet or a Raspberry Pi for hours. Move
things by rewriting one bound property every minute, which is one small patch, rather than animating;
let time advance through a [time reference](/ui/components/time/), which costs no patch at all.

## Input

By default a screensaver is inert: the first touch, click or key press dismisses it, and Macro Deck
swallows that input so it never presses the button that happens to be under the finger. Nothing reaches
your tree.

```csharp
new ScreenSaverDescriptor("player", MyStrings.PlayerName(), Interactive: true);
```

With `Interactive`, a press on a node that claims it - a `ui.button`, a slider - is delivered to your
session as an event; input anywhere else still dismisses the screensaver, and so does Escape.

## Configuration

With `HasConfiguration`, picking your screensaver in a device's settings opens a `config` surface with the
`screensaver-config` entry point. Build it with the [configuration view](/ui/views/configuration/). It
carries `deviceId`, `screenSaverId` and `screenSaverConfiguration`; the values are stored with the device
and handed back on every later `screensaver` surface.

## Device settings, and the idle timer

A user turns the screensaver on per device, picks the idle time and the screensaver. New devices start
with it off, so nothing changes for an existing setup. The idle timer runs on the device, never on the
host: the host stores the settings and serves the surface, so a network hiccup can neither delay waking
up nor start a screensaver on its own. The web client keeps the display awake while a screensaver shows.

## When your integration is not running

A device keeps its screensaver id and configuration whether or not anything provides them. Macro Deck
shows its built-in clock instead, so a device is never left on a blank screen, and re-enabling your
integration restores the selection exactly as it was. Withdrawing only stops the screensaver being offered.

## Over the plugin protocol

Registration is driven from the plugin side, so the host-to-provider direction of the
`screensaver-provider` capability only has to describe the provider and re-read its catalog after a
reconnect:

| Operation | Purpose |
| --- | --- |
| `describe` | The provider's declared name and its current catalog. |
| `screensavers` | The provider's current screensaver catalog, re-read after a reconnect. |

Registering and withdrawing a screensaver travels the other way as the `screensavers` host API:

| Operation | Purpose |
| --- | --- |
| `register` | Registers a screensaver, or replaces one already registered under the same provider-local id. |
| `unregister` | Withdraws a screensaver. Devices still using it keep their stored id and configuration and show the clock until it returns. |

The screensavers themselves are served over the `ui` capability, like every other Macro Deck UI surface.
The manifest permission for the host API is `host:screensavers`.

## At a glance

| Member | Package | What it is |
| --- | --- | --- |
| `IScreenSaverProvider` | SDK | Implemented by an integration that offers screensavers. |
| `ScreenSaverDescriptor` | SDK | One screensaver: `Id`, `Name`, `Description`, `HasConfiguration`, `Interactive`, `Metadata`. |
| `IScreenSaverProviderContext` | SDK | `RegisterScreenSaverAsync` and `UnregisterScreenSaverAsync`. |
| `ScreenSaverRegistration` | SDK | What registering returns: the qualified `ScreenSaverId` and its owner. |
| `UiSurfaceKinds.ScreenSaver` | UI model | The `screensaver` surface kind to declare. |
| `UiScreenSaverSurfaceAttributes` | UI model | `DeviceId`, `ScreenSaverId` and `Configuration` on a `screensaver` surface. |
| `UiConfigEntryPoints.ScreenSaverConfig` | UI model | The `screensaver-config` entry point of a `config` surface. |
| `UiConfigSurfaceAttributes.ScreenSaverId`, `.ScreenSaverConfiguration`, `.DeviceId` | UI model | Which screensaver is being configured and its current values. `DeviceId` is informational: never authorize anything by it. |
| `PluginPermissions.HostScreenSavers` | Packaging | The `host:screensavers` manifest permission. |
| `FakeScreenSaverProviderContext`, `ScreenSaverProviderTestClient` | Plugin testing | See [Testing screensavers](/features/testing/#testing-screensavers). |

## See also

- [Folder views](/ui/views/folder-views/) - the same registration shape, for a whole folder.
- [Serving a view](/ui/views/sessions/)
- [Components](/ui/components/)
