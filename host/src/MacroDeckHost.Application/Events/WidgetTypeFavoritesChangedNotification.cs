using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record WidgetTypeFavoritesChangedNotification(IReadOnlyList<string> TypeIds) : INotification;
