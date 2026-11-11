using Serilog;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost;

public sealed class ShellProcessWatchdog(
	ShellParent parent,
	IHostApplicationLifetime lifetime,
	TimeProvider timeProvider) : BackgroundService
{
	private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(1);
	private static readonly ILogger _logger = Log.ForContext<ShellProcessWatchdog>();

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		if (parent.ExpectedProcessId is not { } expected)
		{
			return;
		}

		using var timer = new PeriodicTimer(_pollInterval, timeProvider);
		do
		{
			if (parent.CurrentParentProcessId() != expected)
			{
				_logger.Warning("Macro Deck (process {ProcessId}) is no longer running; stopping the host", expected);
				lifetime.StopApplication();
				return;
			}
		} while (await timer.WaitForNextTickAsync(stoppingToken));
	}
}
