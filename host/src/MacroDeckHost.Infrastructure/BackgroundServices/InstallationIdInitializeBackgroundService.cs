using MacroDeckHost.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class InstallationIdInitializeBackgroundService : HostReadyBackgroundService
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger _logger;

	public InstallationIdInitializeBackgroundService(
		IHostApplicationLifetime lifetime,
		IServiceScopeFactory scopeFactory,
		ILogger logger)
		: base(lifetime)
	{
		_scopeFactory = scopeFactory;
		_logger = logger;
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceService>();
		var installationId = await preferences.GetInstallationId();
		_logger.Information("Installation id ready: {InstallationId}", installationId);
	}
}
