using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record FolderFocusRuleChangedNotification(Guid FolderId) : INotification;
