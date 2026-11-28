using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.Services;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class StarterProfileBackgroundService : HostReadyBackgroundService
{
	private readonly ProviderDeviceStarterProfiles _starterProfiles;
	private readonly StartupReadiness _readiness;

	public StarterProfileBackgroundService(
		IHostApplicationLifetime lifetime,
		ProviderDeviceStarterProfiles starterProfiles,
		StartupReadiness readiness)
		: base(lifetime)
	{
		_starterProfiles = starterProfiles;
		_readiness = readiness;
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		await _readiness.WhenCachesReady.WaitAsync(stoppingToken);
		await _starterProfiles.ProvidePendingAsync();
	}
}
