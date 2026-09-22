using System.Collections.Concurrent;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Capabilities;

namespace MacroDeckHost.Application.Messaging;

public enum PluginMessagingSyncOutcome
{
	Applied,

	NotDeclared,

	SessionNotCurrent
}

public sealed class PluginMessagingRegistrations : IDisposable
{
	private readonly IMessageBroker _broker;
	private readonly IPluginSessionRegistry _sessionRegistry;
	private readonly IPluginCapabilityInvoker _invoker;
	private readonly IRemotePluginConnectionState _connectionState;
	private readonly ConcurrentDictionary<string, RemotePluginMessageParticipant> _participants = new(StringComparer.Ordinal);
	private readonly Lock _lock = new();

	public PluginMessagingRegistrations(IMessageBroker broker,
		IPluginSessionRegistry sessionRegistry,
		IPluginCapabilityInvoker invoker,
		IRemotePluginConnectionState connectionState)
	{
		_broker = broker;
		_sessionRegistry = sessionRegistry;
		_invoker = invoker;
		_connectionState = connectionState;
		_sessionRegistry.SessionEnded += OnSessionEnded;
	}

	public bool HasDeclaredMessaging(string pluginId)
		=> _sessionRegistry.GetCapabilities(pluginId)?.Capabilities.GetValueOrDefault(CapabilityKinds.Messaging)
			is { Accepted: true };

	public (PluginMessagingSyncOutcome Outcome, IReadOnlyList<MessageRegistrationRejection> Rejected) Replace(
		string pluginId,
		string sessionId,
		MessageRegistrations registrations)
	{
		if (!HasDeclaredMessaging(pluginId))
		{
			return (PluginMessagingSyncOutcome.NotDeclared, []);
		}

		var participant = _participants.GetOrAdd(pluginId,
			id => new RemotePluginMessageParticipant(id, _invoker, _connectionState));

		// The registry maps the plugin to its new session before it announces the old one pruned, so
		// checking and replacing under the lock the removal also takes cannot resurrect a pruned session.
		lock (_lock)
		{
			var current = _sessionRegistry.Snapshot()
				.FirstOrDefault(session => string.Equals(session.PluginId, pluginId, StringComparison.Ordinal));
			if (current is null || !string.Equals(current.SessionId, sessionId, StringComparison.Ordinal))
			{
				return (PluginMessagingSyncOutcome.SessionNotCurrent, []);
			}

			return (PluginMessagingSyncOutcome.Applied, _broker.Replace(pluginId, sessionId, participant, registrations));
		}
	}

	public void Dispose() => _sessionRegistry.SessionEnded -= OnSessionEnded;

	// Detached sessions keep their claims through the resume window; only a pruned or replaced session
	// releases them, and only the entries that session itself registered.
	private void OnSessionEnded(object? sender, PluginSessionEndedEventArgs e)
	{
		if (e.Reason == PluginSessionEndReason.Pruned)
		{
			lock (_lock)
			{
				_broker.Remove(e.PluginId, e.SessionId);
			}
		}
	}
}
