using Mediator;

namespace MacroDeckHost.Application.Events;

/// <summary>Raised only when a pairing request is newly created, unlike
/// <see cref="PluginPairingRequestsChangedNotification" />, which also fires on approve, reject and
/// revoke. Anything that draws the user's attention has to key off this one, or approving a prompt would
/// itself raise a fresh notification.</summary>
public sealed record PluginPairingRequestedNotification(string PluginId, string DisplayName) : INotification;
