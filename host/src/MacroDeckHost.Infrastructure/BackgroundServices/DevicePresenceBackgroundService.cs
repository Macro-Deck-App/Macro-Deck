using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Persistence.Repositories;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class DevicePresenceBackgroundService : HostReadyBackgroundService
{
	private static readonly TimeSpan _tickInterval = TimeSpan.FromSeconds(2);
	private static readonly TimeSpan _touchInterval = TimeSpan.FromMinutes(5);
	private static readonly TimeSpan _purgeInterval = TimeSpan.FromHours(24);

	private readonly DeviceConnectionTracker _tracker;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	private HashSet<Guid> _lastOnline = [];
	private DateTime _nextTouch;
	private DateTime _nextPurge;

	public DevicePresenceBackgroundService(
		IHostApplicationLifetime lifetime,
		DeviceConnectionTracker tracker,
		IServiceScopeFactory scopeFactory,
		TimeProvider timeProvider,
		ILogger logger)
		: base(lifetime)
	{
		_tracker = tracker;
		_scopeFactory = scopeFactory;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<DevicePresenceBackgroundService>();
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		var now = _timeProvider.GetUtcNow().UtcDateTime;
		_nextTouch = now;
		_nextPurge = now + _purgeInterval;

		using var timer = new PeriodicTimer(_tickInterval);
		do
		{
			await SafeTick(stoppingToken);
		} while (await timer.WaitForNextTickAsync(stoppingToken));
	}

	private async Task SafeTick(CancellationToken stoppingToken)
	{
		try
		{
			await Tick(stoppingToken);
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Device presence tick failed");
		}
	}

	private async Task Tick(CancellationToken ct)
	{
		var now = _timeProvider.GetUtcNow().UtcDateTime;
		_tracker.FlushPendingDisconnects(now);

		var onlineIds = _tracker.OnlineDeviceConnectionCounts().Keys.ToHashSet();
		var wentOffline = _lastOnline.Except(onlineIds).ToList();
		var wentOnline = onlineIds.Except(_lastOnline).ToList();
		_lastOnline = onlineIds;

		var touchDue = now >= _nextTouch;
		var purgeDue = now >= _nextPurge;
		if (wentOffline.Count == 0 && wentOnline.Count == 0 && !touchDue && !purgeDue)
		{
			return;
		}

		using var scope = _scopeFactory.CreateScope();
		var repository = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

		if (wentOffline.Count > 0)
		{
			await repository.TouchLastSeen(wentOffline, now);
		}

		foreach (var id in wentOffline)
		{
			await mediator.Publish(new DeviceChangedNotification(id), ct);
			await mediator.Publish(new DevicePresenceChangedNotification(id, Online: false), ct);
		}

		foreach (var id in wentOnline)
		{
			await mediator.Publish(new DeviceChangedNotification(id), ct);
			await mediator.Publish(new DevicePresenceChangedNotification(id, Online: true), ct);
		}

		if (touchDue || wentOnline.Count > 0)
		{
			var devices = await repository.GetAll();
			_tracker.SetDeviceNames(devices.ToDictionary(d => d.Id, d => d.Name));
		}

		if (touchDue)
		{
			if (onlineIds.Count > 0)
			{
				await repository.TouchLastSeen(onlineIds, now);
			}

			_nextTouch = now + _touchInterval;
		}

		if (purgeDue)
		{
			var service = scope.ServiceProvider.GetRequiredService<IDeviceService>();
			await service.PurgeStale(now);
			_nextPurge = now + _purgeInterval;
		}
	}
}
