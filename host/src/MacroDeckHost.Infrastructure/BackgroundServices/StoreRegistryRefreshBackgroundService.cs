using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Store;
using Mediator;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class StoreRegistryRefreshBackgroundService : HostReadyBackgroundService
{
	private static readonly TimeSpan _maxStartupJitter = TimeSpan.FromSeconds(120);

	private readonly IStoreRegistryRefresher _refresher;
	private readonly StoreRegistryOptions _options;
	private readonly IMediator _mediator;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	public StoreRegistryRefreshBackgroundService(IHostApplicationLifetime lifetime,
		IStoreRegistryRefresher refresher,
		StoreRegistryOptions options,
		IMediator mediator,
		TimeProvider timeProvider,
		ILogger logger)
		: base(lifetime)
	{
		_refresher = refresher;
		_options = options;
		_mediator = mediator;
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
			var result = await _refresher.Refresh(stoppingToken);
			if (!result.Success)
			{
				_logger.Warning("Store registry refresh failed with {Error}: {Message}",
					result.Error,
					result.ErrorMessage);
				return;
			}

			await _mediator.Publish(new StoreRegistryRefreshedNotification(_refresher.Status), stoppingToken);
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Store registry refresh threw.");
		}
	}
}
