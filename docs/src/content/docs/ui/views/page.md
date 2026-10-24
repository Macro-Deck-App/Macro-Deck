---
title: Integration pages
description: Adding a tab of your own to your integration's page in the Macro Deck app, served as a page surface.
---

Declare a page to give your integration a tab of its own on its page in the Macro Deck app, beside the
capability tabs Macro Deck draws itself. Use it for something your integration manages as a whole, such as
a library of scripts, rather than for one action or widget.

## Example

```csharp
public sealed class ScriptsUiProvider : IUiProvider
{
    public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
        [new() { Kind = UiSurfaceKinds.Page, SessionMode = UiSessionModes.Exclusive }];

    public IReadOnlyList<UiPageDeclaration> Pages { get; } =
        [new() { Id = "scripts", Title = Strings.Page.Scripts(), IconName = "code" }];

    public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
    {
        if (request.Surface.Kind != UiSurfaceKinds.Page
            || !request.Surface.Attributes.TryGetValue(UiPageSurfaceAttributes.PageId, out var pageId)
            || pageId.GetString() != "scripts")
        {
            return Task.FromResult<IUiSession?>(null);
        }

        return Task.FromResult<IUiSession?>(new ScriptsPageSession(request.Surface));
    }
}
```

## Declaring a page

`IUiProvider.Pages` lists your pages in the order their tabs appear. It is read like `Surfaces`: before
anything is initialized, so keep it free of side effects and independent of a connection or configuration.

| `UiPageDeclaration` | Meaning |
| --- | --- |
| `Id` | Names the page within your integration. A letter or digit, then up to 63 letters, digits, hyphens or underscores. |
| `Title` | The tab's label, localized. |
| `IconName` | A built-in [icon](/ui/components/icon/) shown beside the title, or `null`. |

**Declare a `page` surface too.** Macro Deck shows the pages of a provider only when `Surfaces` contains
the `page` kind, and skips a page whose `Id` is malformed or repeats an earlier one. Conformance check
[MDC0316](/reference/conformance/#mdc0316) reports either mistake.

## Serving the page

When an administrator opens the tab, Macro Deck opens a `page` session on your provider. Its surface
carries `UiPageSurfaceAttributes`:

| Attribute | Value |
| --- | --- |
| `IntegrationId` (`integrationId`) | Your integration's id. |
| `PageId` (`pageId`) | The `Id` of the page the tab shows. Decline an id you do not recognise. |
| `Culture` (`culture`) | The culture the user reads Macro Deck in, for example `de` or `pt-BR`, when Macro Deck knows it. |

- A page session is **exclusive**. Every opening of the tab is its own session, ended when the user leaves
  the tab.
- **Only an administrator's desktop app opens one.** A deck device cannot, and is told the session is
  forbidden before Macro Deck looks at your pages.
- A plugin's page opens only while the plugin is connected; a disabled integration's pages do not open.
- Everything else is an ordinary session: the [lifecycle and limits](/ui/views/sessions/) of every surface
  apply, and inputs, buttons and the [event origin](/ui/views/sessions/) work as in any view. A page that
  edits large text uses a [code input with a content resource](/ui/views/configuration/#code-input).

## A master-detail page

A page that manages a library looks best laid out like Macro Deck's own Scripts and Variables pages: a
[split](/ui/views/configuration/#page-layout) with the items in its rail and the selected one in its detail.
Each page session builds its own view, so each client keeps its own selection:

```csharp
private UiView BuildLibraryView(UiSurface surface)
{
    var selected = new UiState<string?>(null);

    UiElement Row(Note note) => new UiConfigListItem
    {
        Key = note.Id,
        Label = note.Title,
        Description = note.Summary,
        Icon = "file",
        Severity = note.HasErrors ? "error" : "success",
        Selected = UiValue.From(() => selected.Value == note.Id),
        Events = [UiEventHandler.On(UiConfigEvents.Activate, () => selected.Value = note.Id)],
        Children =
        [
            new UiConfigMenu
            {
                Key = "actions",
                Label = Strings.Page.Actions(),
                Children =
                [
                    new UiConfigButton
                    {
                        Key = "delete",
                        Label = Strings.Page.Delete(),
                        Icon = "trash",
                        Variant = UiButtonVariants.Danger,
                        ConfirmMessage = Strings.Page.DeleteQuestion(),
                        ConfirmDanger = true,
                        Events = [UiEventHandler.On(UiConfigEvents.Activate, () => _notes.Remove(note.Id))],
                    },
                ],
            },
        ],
    };

    return new UiView(surface, new UiSplit
    {
        Key = "page",
        RailTitle = Strings.Page.Notes(),
        RailActions =
        [
            new UiConfigButton
            {
                Key = "new",
                Label = Strings.Page.New(),
                Icon = "plus",
                Events = [UiEventHandler.On(UiConfigEvents.Activate, () => selected.Value = _notes.Add())],
            },
        ],
        Rail =
        [
            new UiConfigList
            {
                Key = "notes",
                Label = Strings.Page.Notes(),
                Children =
                [
                    new UiRepeat<Note>
                    {
                        Key = "rows",
                        Items = UiValue.From(() => _notes.All),
                        KeySelector = note => note.Id,
                        Template = (note, _) => Row(note),
                    },
                ],
            },
        ],
        Detail =
        [
            new UiWhen
            {
                Key = "nothing-when",
                Condition = () => selected.Value is null,
                Content = () => new UiEmptyState
                {
                    Key = "nothing",
                    Icon = "file",
                    Title = Strings.Page.NothingSelected(),
                    Text = Strings.Page.PickOrCreate(),
                },
            },
            new UiWhen
            {
                Key = "note-when",
                Condition = () => selected.Value is not null,
                Content = () => new UiConfigStack
                {
                    Key = "note",
                    Gap = UiStackGaps.Large,
                    Children =
                    [
                        new UiHeading
                        {
                            Key = "title",
                            Text = UiText.From(() => _notes.Get(selected.Value!).Title),
                            Badge = UiText.Optional(() => _notes.IsDirty(selected.Value!) ? Strings.Page.Unsaved() : UiText.None()),
                        },
                        new UiStringInput { Key = "body", Multiline = true, Binding = _notes.BodyOf(selected) },
                        new UiConfigStack
                        {
                            Key = "footer",
                            Direction = "horizontal",
                            Align = UiStackAlignments.End,
                            Children =
                            [
                                new UiConfigButton { Key = "discard", Label = Strings.Page.Discard() },
                                new UiConfigButton { Key = "save", Label = Strings.Page.Save(), Variant = UiButtonVariants.Primary },
                            ],
                        },
                    ],
                },
            },
        ],
        Fallback = new UiProse { Key = "update", Text = Strings.Page.UpdateMacroDeck() },
    });
}
```

`_notes` stands for the integration's own store, and `UiRepeat` and `UiWhen` are the usual
[reactive building blocks](/ui/concepts/reactive-updates/). The rail lists every note with a delete action in
its trailing menu, and the detail shows an empty state until a row is chosen. The `Fallback` is what a
Macro Deck release from before the layout types draws instead of the split.

## Compatibility

`Pages` is a default interface member that answers no pages, so a provider written before pages keeps
compiling and declares none. Out of process, the pages travel as the optional `pages` member of
`ui/describe`. A Macro Deck release from before pages ignores both the member and the `page` surface, and
shows no tab.

## See also

- [Serving a view](/ui/views/sessions/)
- [Views and surfaces](/ui/views/)
- [Serving a configuration view](/ui/views/configuration/)
