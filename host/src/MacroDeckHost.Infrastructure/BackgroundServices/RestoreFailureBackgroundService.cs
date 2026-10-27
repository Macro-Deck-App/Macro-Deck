using MacroDeckHost.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class RestoreFailureBackgroundService : HostReadyBackgroundService
{
	private readonly IServiceScopeFactory _scopeFactory;

	public RestoreFailureBackgroundService(IHostApplicationLifetime lifetime, IServiceScopeFactory scopeFactory)
		: base(lifetime)
	{
		_scopeFactory = scopeFactory;
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var notifier = scope.ServiceProvider.GetRequiredService<IRestoreFailureNotifier>();
		await notifier.NotifyIfFailed(stoppingToken);
	}
}
