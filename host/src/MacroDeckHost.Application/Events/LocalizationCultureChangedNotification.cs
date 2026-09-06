using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record LocalizationCultureChangedNotification(string Culture, string FallbackCulture) : INotification;
