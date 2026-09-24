using ILogger = Serilog.ILogger;

namespace MacroDeckHost.CompanionApp;

public sealed class CompanionAppBackgroundService : BackgroundService
{
	internal static readonly TimeSpan FailureDelay = TimeSpan.FromMinutes(1);

	private readonly CompanionAppService _service;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;

	public CompanionAppBackgroundService(CompanionAppService service, TimeProvider time, ILogger logger)
	{
		_service = service;
		_time = time;
		_logger = logger.ForContext<CompanionAppBackgroundService>();
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			DateTimeOffset? next;
			try
			{
				next = await _service.RunDueWorkAsync(stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception ex)
			{
				_logger.Warning(ex, "Companion app work failed; trying again later");
				next = _time.GetUtcNow() + FailureDelay;
			}

			try
			{
				await _service.WaitForWorkAsync(next, stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				return;
			}
		}
	}
}
