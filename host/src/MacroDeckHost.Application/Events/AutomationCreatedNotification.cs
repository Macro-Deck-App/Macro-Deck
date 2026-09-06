using MacroDeckHost.Domain.Entities;
using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record AutomationCreatedNotification(AutomationEntity Automation) : INotification;
