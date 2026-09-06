using MacroDeckHost.Domain.Entities;
using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record FoldersReorderedNotification(Guid ProfileId, IReadOnlyList<FolderEntity> Folders) : INotification;
