using MacroDeckHost.Application.Services;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class AutostartRefreshBackgroundService : HostReadyBackgroundService
{
	private readonly IAutostartService _autostart;

	public AutostartRefreshBackgroundService(IHostApplicationLifetime lifetime, IAutostartService autostart)
		: base(lifetime)
	{
		_autostart = autostart;
	}

	protected override Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		_autostart.RefreshRegistration();
		return Task.CompletedTask;
	}
}
