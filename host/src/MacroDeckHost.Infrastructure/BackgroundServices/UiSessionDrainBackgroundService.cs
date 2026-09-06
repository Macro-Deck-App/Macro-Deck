using MacroDeckHost.Application.Ui.Sessions;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

/// <summary>
/// Closes UI sessions whose drain window has elapsed.
/// </summary>
/// <remarks>
/// Each draining session already arms its own timer; this is the backstop for one that never fired -
/// a session stuck in <c>Draining</c> holds a provider session open with nobody watching it.
/// </remarks>
public sealed class UiSessionDrainBackgroundService : HostReadyBackgroundService
{
	private static readonly TimeSpan _tickInterval = TimeSpan.FromSeconds(5);

	private readonly IUiSessionBroker _broker;
	private readonly ILogger _logger;

	public UiSessionDrainBackgroundService(IHostApplicationLifetime lifetime, IUiSessionBroker broker, ILogger logger)
		: base(lifetime)
	{
		_broker = broker;
		_logger = logger.ForContext<UiSessionDrainBackgroundService>();
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(_tickInterval);

		do
		{
			try
			{
				_broker.SweepDraining();
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				_logger.Error(exception, "Failed to sweep draining UI sessions");
			}
		} while (await timer.WaitForNextTickAsync(stoppingToken));
	}
}
