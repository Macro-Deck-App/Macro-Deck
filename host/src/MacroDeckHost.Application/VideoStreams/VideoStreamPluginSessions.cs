using MacroDeck.Sdk.VideoStreams;
using MacroDeckHost.Application.Plugins;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Application.VideoStreams;

public sealed class VideoStreamPluginSessions : IHostedService
{
	private readonly IVideoStreamSessionBroker _broker;
	private readonly IPluginSessionRegistry _pluginSessions;
	private readonly VideoStreamProviderRegistry _registry;

	public VideoStreamPluginSessions(IPluginSessionRegistry pluginSessions,
		VideoStreamProviderRegistry registry,
		IVideoStreamSessionBroker broker)
	{
		_pluginSessions = pluginSessions;
		_registry = registry;
		_broker = broker;
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		_pluginSessions.SessionEnded += OnSessionEnded;
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		_pluginSessions.SessionEnded -= OnSessionEnded;
		return Task.CompletedTask;
	}

	// Keyed by the plugin session id, never the plugin id alone: a late Pruned for a replaced session
	// arrives after the replacement registered and must not tear down what the replacement owns.
	public void End(string pluginId, string pluginSessionId)
	{
		var current = _pluginSessions.Snapshot()
			.FirstOrDefault(session => string.Equals(session.PluginId, pluginId, StringComparison.Ordinal))
			?.SessionId;

		_broker.ClosePluginSession(pluginSessionId, VideoStreamSessionReason.ProviderRemoved);

		if (current is null || string.Equals(current, pluginSessionId, StringComparison.Ordinal))
		{
			_registry.RemovePluginSession(pluginId, pluginSessionId);
		}
	}

	private void OnSessionEnded(object? sender, PluginSessionEndedEventArgs e) => End(e.PluginId, e.SessionId);
}
