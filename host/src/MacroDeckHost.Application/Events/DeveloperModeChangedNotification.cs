using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record DeveloperModeChangedNotification(bool Enabled) : INotification;
