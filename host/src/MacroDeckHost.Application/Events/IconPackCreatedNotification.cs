using MacroDeckHost.Domain.Entities;
using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record IconPackCreatedNotification(IconPackEntity Pack, int IconCount) : INotification;
