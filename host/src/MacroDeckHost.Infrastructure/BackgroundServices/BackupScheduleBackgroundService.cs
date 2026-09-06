using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Backups;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class BackupScheduleBackgroundService : HostReadyBackgroundService
{
	private static readonly TimeSpan _maximumSleep = TimeSpan.FromHours(1);

	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IBackupService _backups;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;

	public BackupScheduleBackgroundService(IHostApplicationLifetime lifetime,
		IServiceScopeFactory scopeFactory,
		IBackupService backups,
		TimeProvider time,
		ILogger logger)
		: base(lifetime)
	{
		_scopeFactory = scopeFactory;
		_backups = backups;
		_time = time;
		_logger = logger.ForContext<BackupScheduleBackgroundService>();
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		try
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				var sleep = await Tick(stoppingToken);
				await Task.Delay(sleep, _time, stoppingToken);
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
		}
	}

	private async Task<TimeSpan> Tick(CancellationToken cancellationToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceService>();

		var settings = await preferences.GetBackups();
		var lastRun = await preferences.GetBackupScheduleLastRun();
		var now = _time.GetUtcNow();
		var due = BackupScheduleCalculator.NextDue(settings, lastRun, now);

		if (due is null)
		{
			return _maximumSleep;
		}

		if (due.Value > now)
		{
			return Clamp(due.Value - now);
		}

		// Stamped whether or not the backup ran. A trigger that is skipped because another operation holds
		// the gate must not re-fire on the next tick, or a busy host would retry in a tight loop.
		await preferences.SetBackupScheduleLastRun(now);

		var result = await _backups.Create(new CreateBackupRequest(BackupTrigger.Scheduled), cancellationToken);
		if (!result.Success)
		{
			_logger.Warning("The scheduled backup did not run: {Error} {Message}",
				result.Error,
				result.ErrorMessage);
		}

		return Clamp(_maximumSleep);
	}

	private static TimeSpan Clamp(TimeSpan value)
		=> value < TimeSpan.Zero ? TimeSpan.Zero : value > _maximumSleep ? _maximumSleep : value;
}
