using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record ScriptDeletedNotification(Guid ScriptId) : INotification;
