using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record FolderFocusRuleRemovedNotification(Guid FolderId, Guid RuleId) : INotification;
