using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record VirtualProfilesAnnouncedNotification(string IntegrationId) : INotification;
