using MacroDeckHost.Application.Announcements;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class AnnouncementRefreshBackgroundService : HostReadyBackgroundService
{
	public static readonly TimeSpan MaxStartupJitter = TimeSpan.FromSeconds(30);
	public static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);
	public static readonly TimeSpan MaxIntervalJitter = TimeSpan.FromMinutes(30);

	private readonly IAnnouncementService _announcements;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	public AnnouncementRefreshBackgroundService(IHostApplicationLifetime lifetime,
		IAnnouncementService announcements,
		TimeProvider timeProvider,
		ILogger logger)
		: base(lifetime)
	{
		_announcements = announcements;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<AnnouncementRefreshBackgroundService>();
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		await Task.Delay(Jitter(MaxStartupJitter), _timeProvider, stoppingToken);

		while (true)
		{
			await Refresh(stoppingToken);
			await Task.Delay(RefreshInterval + Jitter(MaxIntervalJitter), _timeProvider, stoppingToken);
		}
	}

	private async Task Refresh(CancellationToken stoppingToken)
	{
		try
		{
			await _announcements.Refresh(stoppingToken);
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Checking for a new announcement failed.");
		}
	}

	private static TimeSpan Jitter(TimeSpan maximum)
		=> TimeSpan.FromMilliseconds(Random.Shared.NextInt64((long)maximum.TotalMilliseconds));
}
