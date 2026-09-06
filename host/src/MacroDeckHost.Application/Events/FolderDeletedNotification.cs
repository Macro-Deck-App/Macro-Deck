using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record FolderDeletedNotification(Guid FolderId) : INotification;
