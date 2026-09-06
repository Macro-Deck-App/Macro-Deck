using MacroDeckHost.Application.Devices;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

/// <summary>
/// Closes the UI WebSocket connections when the host starts shutting down.
/// Kestrel treats them as ordinary in-flight requests and waits out the full graceful
/// shutdown timeout otherwise, which turns a requested restart into a timed-out shutdown
/// and costs the restart exit code the shell relies on.
/// </summary>
public sealed class UiConnectionShutdownBackgroundService : IHostedService
{
	private readonly DeviceConnectionTracker _connections;
	private readonly IHostApplicationLifetime _lifetime;

	public UiConnectionShutdownBackgroundService(
		DeviceConnectionTracker connections,
		IHostApplicationLifetime lifetime)
	{
		_connections = connections;
		_lifetime = lifetime;
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		// ApplicationStopping fires before the hosted services stop, so clients are cut loose
		// as early as possible and reconnect against the restarted host instead of this one.
		_lifetime.ApplicationStopping.Register(() => _connections.AbortAll());
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		_connections.AbortAll();
		return Task.CompletedTask;
	}
}
