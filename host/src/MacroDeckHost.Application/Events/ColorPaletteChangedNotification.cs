using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record ColorPaletteChangedNotification(IReadOnlyList<string> Colors) : INotification;
