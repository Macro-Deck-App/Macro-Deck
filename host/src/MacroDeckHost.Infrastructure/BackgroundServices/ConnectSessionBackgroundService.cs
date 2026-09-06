using MacroDeckHost.Infrastructure.Connect;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class ConnectSessionBackgroundService : HostReadyBackgroundService
{
	// The refresh token slides on every use over a 180-day window, so an installation that never asks for
	// a token still has to touch the endpoint often enough to keep the authorization alive.
	private static readonly TimeSpan _keepAliveInterval = TimeSpan.FromHours(24);

	private readonly ConnectSessionService _sessionService;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	public ConnectSessionBackgroundService(
		IHostApplicationLifetime lifetime,
		ConnectSessionService sessionService,
		TimeProvider timeProvider,
		ILogger logger)
		: base(lifetime)
	{
		_sessionService = sessionService;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<ConnectSessionBackgroundService>();
	}

	public override async Task StopAsync(CancellationToken cancellationToken)
	{
		await base.StopAsync(cancellationToken);
		await _sessionService.CompleteAsync();
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		try
		{
			await _sessionService.Initialize(stoppingToken);

			while (!stoppingToken.IsCancellationRequested)
			{
				await Task.Delay(_keepAliveInterval, _timeProvider, stoppingToken);
				await _sessionService.KeepAlive(stoppingToken);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "The Macro Deck Connect session service stopped unexpectedly");
		}
	}
}
