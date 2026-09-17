using MacroDeckHost.Application.Store;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class StoreRegistryRefreshBackgroundService : HostReadyBackgroundService
{
	private static readonly TimeSpan _maxStartupJitter = TimeSpan.FromSeconds(5);

	private readonly IStoreRegistryRefresher _refresher;
	private readonly StoreRegistryOptions _options;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	public StoreRegistryRefreshBackgroundService(IHostApplicationLifetime lifetime,
		IStoreRegistryRefresher refresher,
		StoreRegistryOptions options,
		TimeProvider timeProvider,
		ILogger logger)
		: base(lifetime)
	{
		_refresher = refresher;
		_options = options;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<StoreRegistryRefreshBackgroundService>();
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		await _refresher.LoadCachedRegistry(stoppingToken);

		// Spread first refreshes so many hosts starting together do not arrive at the origin at once.
		await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next((int)_maxStartupJitter.TotalMilliseconds)),
			_timeProvider,
			stoppingToken);

		using var timer = new PeriodicTimer(_options.RefreshInterval, _timeProvider);
		do
		{
			await Refresh(stoppingToken);
		} while (await timer.WaitForNextTickAsync(stoppingToken));
	}

	private async Task Refresh(CancellationToken stoppingToken)
	{
		try
		{
			var result = await _refresher.Refresh(StoreRegistryRefreshTrigger.Scheduled, stoppingToken);
			if (!result.Success)
			{
				_logger.Warning("Store registry refresh failed with {Error}: {Message}",
					result.Error,
					result.ErrorMessage);
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			throw;
		}
		// The refresher stops on ApplicationStopping, which fires before stoppingToken is cancelled.
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Store registry refresh threw.");
		}
	}
}
