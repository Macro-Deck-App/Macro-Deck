using MacroDeckHost.Application.Services;
using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record AdbSettingsChangedNotification(AdbSettings Settings) : INotification;
