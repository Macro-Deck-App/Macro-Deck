using MacroDeckHost.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class PublicListenerUnavailableBackgroundService : HostReadyBackgroundService
{
	private readonly IServiceScopeFactory _scopeFactory;

	public PublicListenerUnavailableBackgroundService(
		IHostApplicationLifetime lifetime,
		IServiceScopeFactory scopeFactory)
		: base(lifetime)
	{
		_scopeFactory = scopeFactory;
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var notifier = scope.ServiceProvider.GetRequiredService<IPublicListenerUnavailableNotifier>();
		await notifier.NotifyIfUnavailable(stoppingToken);
	}
}
