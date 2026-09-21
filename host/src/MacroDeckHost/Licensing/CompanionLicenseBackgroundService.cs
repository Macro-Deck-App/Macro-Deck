using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Licensing;

public sealed class CompanionLicenseBackgroundService : BackgroundService
{
	internal static readonly TimeSpan FailureDelay = TimeSpan.FromMinutes(1);

	private readonly CompanionLicenseService _licenses;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;

	public CompanionLicenseBackgroundService(CompanionLicenseService licenses, TimeProvider time, ILogger logger)
	{
		_licenses = licenses;
		_time = time;
		_logger = logger.ForContext<CompanionLicenseBackgroundService>();
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			DateTimeOffset? next;
			try
			{
				next = await _licenses.RunDueWorkAsync(stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception ex)
			{
				_logger.Warning(ex, "Companion license work failed; trying again later");
				next = _time.GetUtcNow() + FailureDelay;
			}

			try
			{
				await _licenses.WaitForWorkAsync(next, stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				return;
			}
		}
	}
}
