using MacroDeckHost.Domain.Entities;
using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record WidgetCreatedNotification(WidgetEntity Widget) : INotification;
