---
title: Serving a configuration view
description: Rendering a config flow or an action's configuration as a Macro Deck UI tree beside the declared fields it never replaces.
---

Serve a tree for an integration's config flow or one configured action instance, beside the declared
fields it never replaces.

## Example

A config flow whose step is also drawn as a tree. The input keys match the declared field names:

```csharp
public sealed class MediaServerIntegration : IPluginIntegration, IUiConfigFlowProvider
{
    public IReadOnlyList<IActionDefinition> Actions { get; } = [new ToggleAction()];

    public IConfigFlow CreateConfigFlow() => new MediaServerConfigFlow();

    public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

    public Task ShutdownAsync() => Task.CompletedTask;
}

public sealed class MediaServerConfigFlow : IUiConfigFlow
{
    private static ConfigFlowStep ConnectionStep() => new()
    {
        StepId = "connection",
        Title = Strings.Setup.ConnectionTitle(),
        Fields =
        [
            ActionParameter.Secret("api_key", label: Strings.Setup.ApiKey(), required: true),
            ActionParameter.Number("poll_seconds", label: Strings.Setup.PollInterval(), min: 5, max: 3600,
                defaultValue: 30),
        ],
    };

    public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
        => Task.FromResult(ConfigFlowResult.Step(ConnectionStep()));

    public async Task<ConfigFlowResult> SubmitAsync(string stepId, IReadOnlyDictionary<string, object?> input,
        IConfigFlowContext context, CancellationToken cancellationToken)
    {
        var apiKey = input.GetValueOrDefault("api_key") as string ?? string.Empty;

        return await MediaServerClient.CanConnectAsync(apiKey, cancellationToken)
            ? ConfigFlowResult.Complete("Media server")
            : ConfigFlowResult.Error(ConnectionStep(), Strings.Setup.CannotConnect());
    }

    public Task<IUiSession?> CreateUiSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
    {
        var apiKey = new UiState<string>(string.Empty);
        var pollSeconds = new UiState<double>(30);

        var root = new UiConfigStack
        {
            Key = "root",
            Children =
            [
                new UiSecretInput
                {
                    Key = "api_key",
                    Label = Strings.Setup.ApiKey(),
                    Required = true,
                    Binding = Bind.To(apiKey),
                },
                new UiNumberInput
                {
                    Key = "poll_seconds",
                    Label = Strings.Setup.PollInterval(),
                    Description = Strings.Setup.PollIntervalHint(),
                    Min = 5,
                    Max = 3600,
                    ShowSlider = true,
                    Binding = Bind.To(pollSeconds),
                },
            ],
        };

        return Task.FromResult<IUiSession?>(new ViewSession(new UiView(request.Surface, root)));
    }
}
```

`ViewSession` is the adapter from [Serving a view](/ui/views/sessions/#example).

## The tree renders a transaction it does not own

```csharp
// Declared fields stay - they are the fallback and they mark secrets.
ActionParameter.Secret("api_key", label: Strings.Setup.ApiKey(), required: true)

// The tree's top-level input with the same key feeds the same submit.
new UiSecretInput { Key = "api_key", Label = Strings.Setup.ApiKey(), Binding = Bind.To(apiKey) }
```

A configuration tree never completes a flow and never persists a parameter. `SubmitAsync` stays the only
way a flow accepts values, and the ordinary save path the only way an action instance is written - which
keeps secret encryption, OAuth and entry replacement unchanged
([ADR 0050](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0050-ui-sessions-are-host-brokered.md)).

- **Keep serving the declared fields.** `ConfigFlowStep.Fields` and `IActionDefinition.Parameters` are
  still required: they are the fallback, and for a config flow they tell Macro Deck which submitted values
  are secret.
- **Name top-level inputs after their fields.** A top-level input's node id is the field key it submits.

For a config flow, Macro Deck draws the dialog's Continue and Cancel buttons itself, so the tree needs no
submit control of its own:

- **Continue submits what the tree shows.** When the user presses it, the current value of each input whose
  id is a declared field name is sent to `SubmitAsync`, including a value the user typed and one you patched
  in. Inputs whose id is not a declared field name are not picked up this way.
- **`UiFlow.CanSubmit` gates Continue when you set it.** Leave it unset and Continue is enabled once every
  visible required declared field has a value in the tree. Either way, a `ConfigFlowResult.Error` from
  `SubmitAsync` is shown in the dialog.
- **The tree is not seeded from the stored entry.** On a reconfigure your flow is not handed the existing
  values, so the tree shows - and Continue submits - whatever you built it with. Seed it from your own live
  configuration if a reconfigure should start from the current values.

## From a config flow

```csharp
public sealed class MediaServerIntegration : IPluginIntegration, IUiConfigFlowProvider { /* ... */ }

public sealed class MediaServerConfigFlow : IUiConfigFlow
{
    public Task<IUiSession?> CreateUiSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
        => Task.FromResult<IUiSession?>(new ViewSession(new UiView(request.Surface, BuildTree())));
}
```

`IUiConfigFlowProvider` goes on the integration; `describe` then reports `servesConfigUiTree`.
`IUiConfigFlow` goes on the flow object itself, so the tree's state and `SubmitAsync`'s state are one
thing. Macro Deck relays the tree without inspecting it, so it cannot tell which tree values are secret:
**a flow serving a tree classifies its own secrets** by returning them as `ConfigFlowValue.Secret` from
`Complete`.

`CreateUiSessionAsync` can be called more than once for the same flow: Macro Deck opens a new session after
a session ended with a retryable error, and after .NET Hot Reload updated your plugin. Build each session
from state the flow holds, not from anything the previous session kept. The user's unsaved edits reach the
new session as `change` events, the same way they reached the first one.

## From an action

```csharp
public sealed class ToggleAction : IUiConfigurableActionDefinition
{
    public string Id => "toggle";

    public LocalizedText Name => Strings.Toggle.Name();

    public LocalizedText Description => Strings.Toggle.Description();

    public IReadOnlyList<ActionParameter> Parameters { get; } =
    [
        ActionParameter.Text("target", label: Strings.Toggle.Target(), required: true),
        ActionParameter.Text("mode", label: Strings.Toggle.Mode(), defaultValue: "toggle"),
    ];

    public IActionExecutor CreateExecutor() => new ToggleExecutor();

    public Task<IUiSession?> CreateConfigurationSessionAsync(ActionConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        var target = new UiState<string>(Read(request, "target") ?? string.Empty);
        var mode = new UiState<string>(Read(request, "mode") ?? "toggle");

        var root = new UiConfigStack
        {
            Key = "root",
            Children =
            [
                new UiStringInput { Key = "target", Label = Strings.Toggle.Target(), Required = true, Binding = Bind.To(target) },
                new UiChoiceInput
                {
                    Key = "mode",
                    Label = Strings.Toggle.Mode(),
                    Segmented = true,
                    Binding = Bind.To(mode),
                    Options = UiValue.Of<IReadOnlyList<UiOption>>([
                        UiOption.Of("on", Strings.Toggle.ModeOn()),
                        UiOption.Of("off", Strings.Toggle.ModeOff()),
                        UiOption.Of("toggle", Strings.Toggle.ModeToggle()),
                    ]),
                },
            ],
        };

        return Task.FromResult<IUiSession?>(new ViewSession(new UiView(request.Session.Surface, root)));
    }

    private static string? Read(ActionConfigurationRequest request, string name)
        => request.Parameters.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
```

One session is created per open configuration surface, so several can be live for the same action at
once - keep state on the session, never on the definition. `request.Parameters` holds the instance's
stored values, keyed by parameter name. `Secret` and `Password` parameters arrive **masked**
(`UiConfigSurfaceAttributes.MaskedSecretValue`, `"$masked"`), because opening a configuration surface is
not an intent to reveal a secret. You get the real value the ordinary way, when the user submits.

## Letting the user pick a file, folder or image

For a path, use a path input rather than a `UiStringInput`, so the user can browse instead of typing:

```csharp
var folder = new UiState<string>(string.Empty);
var placeholder = new UiState<string>(string.Empty);

new UiConfigStack
{
    Key = "root",
    Children =
    [
        new UiFolderInput { Key = "folder", Label = Strings.Frame.Folder(), Required = true, Binding = Bind.To(folder) },
        new UiImageInput
        {
            Key = "placeholder",
            Label = Strings.Frame.Placeholder(),
            Description = Strings.Frame.PlaceholderHint(),
            FileExtensions = UiValue.Of<IReadOnlyList<string>>(["png", "jpg", "webp"]),
            Binding = Bind.To(placeholder),
        },
    ],
}
```

| Node type | DSL element | Value |
| --- | --- | --- |
| `folder` | `UiFolderInput` | A folder path |
| `file` | `UiFileInput` | A file path, optionally limited to `FileExtensions` |
| `image` | `UiImageInput` | An image file path, limited to image formats unless `FileExtensions` says otherwise |

The value is a plain path string on the computer that runs Macro Deck, not on the device the user is
looking from. The path inputs work in every configuration surface on this page, including widget, folder
view and screensaver configuration.

- **`FileExtensions` are bare extensions, without the dot**: `["png", "jpg"]`, not `[".png"]`. They narrow
  what browsing and dropping offer. Leave them unset and a file input accepts any file, while an image
  input offers the image formats the renderer can draw (PNG, JPEG, GIF, WebP, SVG). Setting them on an
  image input replaces that list rather than adding to it.
- **Macro Deck does not check the path.** The user can type or paste any value, including one outside
  `FileExtensions`, and a file can be moved or deleted after it was picked. Handle a missing file where you
  read it.
- `Label`, `Description` and `Placeholder` apply as on any input, and `Required` marks the label. Without a
  `Placeholder`, the renderer shows its own hint for the kind of path.

The Macro Deck desktop editor draws each as a text field with a Browse button. In the desktop app, Browse
opens the operating system's file or folder dialog; where that is not available, it opens Macro Deck's
own file browser. A file or folder dropped onto the field fills it in, if it matches the input. The image
input shows no preview of the picked file.

In a declared field list, the counterparts are `ActionParameter.File` (with `fileExtensions`),
`ActionParameter.Folder` and `ActionParameter.Image`, which takes no extensions and always offers the
image formats.

## Showing what governs a setting

`UiStatus` is a compact, framed line for "this setting is currently controlled by something else": an
optional leading `Icon`, a muted `Label` and the emphasized `Value` it introduces. Its children are drawn
trailing the text; an icon-only `UiConfigButton` there becomes a borderless control, typically the one that
stops what the line names.

```csharp
var stop = new UiConfigButton
{
    Key = "stop",
    Label = "Stop using",
    Icon = "x",
    Events = [UiEventHandler.On(UiConfigEvents.Activate, StopUsingProvider)],
};

new UiStatus
{
    Key = "provider",
    Icon = "zap",
    Label = "Provided by",
    Value = "Mute / Unmute",
    Children = [stop],
    Fallback = new UiConfigStack
    {
        Key = "provider-fallback",
        Children = [new UiProse { Key = "provider-text", Text = "Provided by Mute / Unmute" }, stop with { Key = "stop-text", Icon = default }],
    },
}
```

A renderer that predates `status` declines it like any unknown type and draws the `Fallback`, so give it one
built from a `UiProse` and the same button where the line matters.

## Asking before a button acts

A `UiConfigButton` can ask first. With `ConfirmMessage` set, the renderer shows a dialog (`ConfirmTitle`,
`ConfirmLabel`, and `ConfirmDanger` for a destructive action) and raises `activate` only when the user
accepts. With `PromptValue` set, the dialog asks for text instead, starting from that value with
`Placeholder` as its hint, and `activate` carries the entered text as a string payload:

```csharp
new UiConfigButton
{
    Key = "rename",
    Label = "Rename",
    ConfirmLabel = "Save",
    PromptValue = UiValue.From(() => current.Value),
    Events = [UiEventHandler.On(UiConfigEvents.Activate, data =>
    {
        if (data.TryGetString(out var name) && name.Trim().Length > 0)
        {
            current.Value = name.Trim();
        }
    })],
}
```

A renderer that predates these properties raises `activate` at once and without a payload, so a handler
has to tolerate a missing answer, and an action that cannot be undone should not rely on the question alone.

## Grouping actions in a menu

`UiConfigMenu` is a compact trigger (`Icon`, with `Label` as its accessible name) that opens a list of its
child `UiConfigButton`s, each drawn with its icon and label and each asking first if it declares a question.
A renderer that predates `menu` declines it and draws the node's `Fallback`.

## Putting a question to the user

`UiConfigDialog` is a modal dialog shown for as long as the node is in the tree, so a provider opens it by
adding it inside a `UiWhen` and closes it by removing it. `Title` is the heading and `Text` the message; child
`UiConfigButton`s are the answers, drawn in order with the last one as the primary answer (or as a warning
when it declares `ConfirmDanger`), and other children such as boolean inputs are the dialog's body. Closing the
dialog without answering raises `cancel` on it:

```csharp
new UiWhen
{
    Key = "stop-when",
    Condition = () => asking.Value,
    Content = () => new UiConfigDialog
    {
        Key = "stop",
        Title = "Stop using this provider?",
        Text = "Your own states come back.",
        Events = [UiEventHandler.On(UiConfigEvents.Cancel, () => asking.Value = false)],
        Children =
        [
            new UiConfigButton { Key = "keep", Label = "Cancel", Events = [UiEventHandler.On(UiConfigEvents.Activate, () => asking.Value = false)] },
            new UiConfigButton { Key = "stop-now", Label = "Stop using", ConfirmDanger = true, Events = [UiEventHandler.On(UiConfigEvents.Activate, Stop)] },
        ],
    },
}
```

A renderer that predates `dialog` declines it and draws its fallback in place.

## Declining and fallback

```csharp
public Task<IUiSession?> CreateUiSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
    => Task.FromResult<IUiSession?>(null); // the client renders the declared fields
```

| What happens | What the user gets |
| --- | --- |
| You return `null` | The declared fields. Not an error. |
| The provider times out, disconnects or trips a session limit | The declared fields, never an error. |
| The client cannot render your UI model version | The declared fields. Negotiated before any session opens - no session, no slot. |

A client renders one representation, never a mix of both.

## Entry points

A `config` surface names its entry point in `UiConfigSurfaceAttributes.EntryPoint`:

| `UiConfigEntryPoints` | Attributes | Served by |
| --- | --- | --- |
| `IntegrationConfig` (`integration-config`) | `IntegrationId`, `ConfigFlowSessionId` | `IUiConfigFlow.CreateUiSessionAsync` |
| `ActionConfig` (`action-config`) | `ActionId`, `Parameters` | `IUiConfigurableActionDefinition.CreateConfigurationSessionAsync` |
| `FolderViewConfig` (`folder-view-config`) | `FolderId`, `FolderViewId`, `FolderViewConfiguration` | Your `IUiProvider` - see [Folder views](/ui/views/folder-views/) |
| `ScreenSaverConfig` (`screensaver-config`) | `DeviceId`, `ScreenSaverId`, `ScreenSaverConfiguration` | Your `IUiProvider` - see [Screensavers](/ui/views/screensavers/#configuration) |
| `WidgetConfig` (`widget-config`) | `WidgetId`, `WidgetType`, `WidgetData`, `WidgetWidth`, `WidgetHeight` | Your `IUiProvider` - see [Configuring a widget](/ui/views/widget-configuration/) |

Out of process, `MacroDeck.Plugin.Hosting` routes `integration-config` and `action-config` to the flow or
action first; if that target does not exist, serves no tree or declines, it falls through to your
`IUiProvider`s. Folder views and widgets have no declared field list, so their rules differ - see their
pages.

## See also

- [Setup flows](/features/setup-flows/)
- [Actions](/features/actions/)
- [Serving a view](/ui/views/sessions/)
- [Configuring a widget](/ui/views/widget-configuration/)
