using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.Plugins;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

/// <summary>
/// Takes a plugin's devices offline when its session ends. Every end reason counts, detach included: a
/// plugin that is not connected cannot serve its hardware, whether or not it may come back. The devices
/// themselves are retained, so a resumed or restarted plugin re-registers the same devices.
/// </summary>
public sealed class PluginDeviceSessionWatcher : IHostedService
{
	private readonly IPluginSessionRegistry _sessions;
	private readonly IPluginDeviceRegistry _devices;
	private readonly ILogger _logger;

	public PluginDeviceSessionWatcher(
		IPluginSessionRegistry sessions,
		IPluginDeviceRegistry devices,
		ILogger logger)
	{
		_sessions = sessions;
		_devices = devices;
		_logger = logger.ForContext<PluginDeviceSessionWatcher>();
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

	private void OnSessionEnded(object? sender, PluginSessionEndedEventArgs e) => _ = WithdrawAsync(e.PluginId);

	private async Task WithdrawAsync(string pluginId)
	{
		try
		{
			await _devices.UnregisterAllAsync(pluginId);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Error(exception,
				"Failed to take the devices of plugin '{PluginId}' offline after its session ended",
				pluginId);
		}
	}
}
