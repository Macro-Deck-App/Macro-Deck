using MacroDeck.Sdk.Profiles;
using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record VirtualProfilesChangedNotification(
	string IntegrationId,
	IReadOnlyList<VirtualProfileDescriptor> Previous,
	IReadOnlyList<VirtualProfileDescriptor> Current) : INotification;
