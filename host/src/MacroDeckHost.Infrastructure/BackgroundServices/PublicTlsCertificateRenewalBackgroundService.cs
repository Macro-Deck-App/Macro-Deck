using MacroDeckHost.Application.Network.Tls;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class PublicTlsCertificateRenewalBackgroundService : HostReadyBackgroundService
{
	private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(12);

	private readonly PublicTlsBootstrapper _bootstrapper;
	private readonly ILogger _logger;

	public PublicTlsCertificateRenewalBackgroundService(IHostApplicationLifetime lifetime,
		PublicTlsBootstrapper bootstrapper,
		ILogger logger)
		: base(lifetime)
	{
		_bootstrapper = bootstrapper;
		_logger = logger;
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(CheckInterval);

		while (await timer.WaitForNextTickAsync(stoppingToken))
		{
			// Catches the two things that outdate a host certificate while the host keeps running: the
			// machine picking up a new address, and the certificate approaching its expiry. The authority
			// is untouched either way, so no device has to trust anything again.
			//
			// The key ring is never locked here: Startup freezes every background service but the
			// loopback port file while it is, so this one does not run at all in that state.
			var result = _bootstrapper.Run(keyRingLocked: false, DateTimeOffset.UtcNow);
			if (result.CertificateChanged)
			{
				_logger.Information("Renewed the public TLS certificate ({Action})", result.Action);
			}
		}
	}
}
