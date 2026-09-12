---
title: Views and surfaces
description: What a view and a surface are, the surface kinds Macro Deck offers, session modes, and which representation a client renders.
---

A view is rendered inside a host-owned **session**, against one **surface** - a place in the app a tree can
appear.

## Example

```csharp
public sealed class WeatherUiProvider : IUiProvider
{
    public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
    [
        new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
        new() { Kind = UiSurfaceKinds.Folder, SessionMode = UiSessionModes.Shared },
        new() { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
    ];

    public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
        => Task.FromResult<IUiSession?>(null); // one branch per kind - see Serving a view
}
```

## Surface kinds

| Kind | What it is | Who opens it | Session mode | Page |
| --- | --- | --- | --- | --- |
| `config` | An integration's config flow, a configured action instance, a folder's view settings, or a widget's settings | The user, from a configuration dialog | exclusive | [Serving a configuration view](/ui/views/configuration/), [Configuring a widget](/ui/views/widget-configuration/) |
| `widget` | A deck widget | Every client showing the deck | shared | [Deck widget views](/ui/views/widget/) |
| `preview` | A read-only rendering of a widget's unsaved draft, such as in the editor or the widget picker | The widget editor | shared | [Deck widget views](/ui/views/widget/) |
| `folder` | A whole folder that selected a custom view | Every client showing the folder | shared | [Folder views](/ui/views/folder-views/) |
| `dialog` | A modal an action opened | The action | exclusive | [Modal views](/ui/views/modal/) |
| `developer-preview` | One `[UiPreview]` scenario | Developer Tools | exclusive | [Developer preview](/ui/views/developer-preview/) |

- **shared** - several clients may be attached at once, such as the same widget on two devices. You never
  address one client, except through an event's `clientId`.
- **exclusive** - one client owns the session, so another client's attach cannot reset entered values in
  a `dialog` or `config` tree.

The tree, the patch and the session are transport-neutral `MacroDeck.Ui.Model` vocabulary. The surface
kind is Macro Deck's own and is open, like node types: decline a kind you do not recognise by returning
`null` - that is correct, not an error.

## Which representation a client renders

| Surface | When the tree is declined or fails |
| --- | --- |
| `config` for a config flow or an action | The client renders your declared `ConfigFlowStep.Fields` or `IActionDefinition.Parameters` instead. |
| `config` for a widget | Only the editor's JSON mode is left - see [Configuring a widget](/ui/views/widget-configuration/#declining). |
| `widget`, `folder`, `dialog` | Nothing is rendered. There is no non-tree fallback. |

A client renders one representation, never a mix. It negotiates the UI model version locally before a
session is opened, so a client that cannot render your tree costs you no session and no slot.

## See also

- [Serving a view](/ui/views/sessions/) - the session lifecycle every kind shares
- [The UI model](/ui/concepts/ui-model/)
