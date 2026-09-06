using MacroDeckHost.Domain.Entities;
using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record IconUpdatedNotification(IconEntity Icon) : INotification;
