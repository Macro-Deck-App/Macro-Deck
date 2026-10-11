using MacroDeckHost.Application.Batteries;
using MacroDeckHost.Application.Plugins;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

// Keyed by session, so an old session's late end event never withdraws what a newer session of the
// same plugin published or resynchronized.
public sealed class PluginBatterySessionWatcher : IHostedService
{
	private readonly IPluginSessionRegistry _sessions;
	private readonly IBatteryRegistry _batteries;
	private readonly ILogger _logger;

	public PluginBatterySessionWatcher(IPluginSessionRegistry sessions, IBatteryRegistry batteries, ILogger logger)
	{
		_sessions = sessions;
		_batteries = batteries;
		_logger = logger.ForContext<PluginBatterySessionWatcher>();
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		_sessions.SessionEnded += OnSessionEnded;
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		_sessions.SessionEnded -= OnSessionEnded;
		return Task.CompletedTask;
	}

	private void OnSessionEnded(object? sender, PluginSessionEndedEventArgs e)
	{
		try
		{
			_batteries.RemoveSession(e.PluginId, e.SessionId);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Error(exception,
				"Failed to withdraw the battery devices of plugin '{PluginId}' after its session ended",
				e.PluginId);
		}
	}
}
