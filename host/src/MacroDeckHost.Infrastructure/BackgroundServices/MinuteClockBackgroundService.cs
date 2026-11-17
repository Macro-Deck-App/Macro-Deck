using MacroDeckHost.Application.Events;
using Mediator;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class MinuteClockBackgroundService : BackgroundService
{
	private readonly IMediator _mediator;
	private readonly TimeProvider _clock;
	private readonly ILogger _logger;

	public MinuteClockBackgroundService(IMediator mediator, TimeProvider clock, ILogger logger)
	{
		_mediator = mediator;
		_clock = clock;
		_logger = logger.ForContext<MinuteClockBackgroundService>();
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var due = NextMinute(_clock.GetLocalNow());
		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				var now = _clock.GetLocalNow();
				if (due - now > TimeSpan.FromMinutes(1))
				{
					due = NextMinute(now);
				}

				var remaining = due - now;
				if (remaining > TimeSpan.Zero)
				{
					await Task.Delay(remaining, _clock, stoppingToken);
					continue;
				}

				due = NextMinute(now);
				await _mediator.Publish(new MinuteElapsedNotification(), stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Failed to announce the minute to time-based templates");
			}
		}
	}

	private static DateTimeOffset NextMinute(DateTimeOffset localNow)
	{
		return localNow.AddTicks(-(localNow.Ticks % TimeSpan.TicksPerMinute)).AddMinutes(1);
	}
}
