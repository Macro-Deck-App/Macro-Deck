using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record AppearanceChangedNotification(string ThemeMode, string AccentColor, string FontFamily) : INotification;
