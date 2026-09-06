using MacroDeck.Plugin.Hosting.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;

namespace MacroDeck.Plugin.Hosting.Transport;

// A killed host never sends the SupervisorShutdown close, so the socket just drops and the reconnect
// loop retries forever. Managed only: a self-registering plugin must outlive the host, as Fault holds.
internal sealed class HostLivenessHostedService(
	IOptions<PluginHostOptions> options,
	PluginRegistrationModeAccessor registrationMode,
	IHostApplicationLifetime lifetime,
	TimeProvider timeProvider,
	ILogger logger) : BackgroundService
{
	private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

	private readonly ILogger _logger = logger.ForContext<HostLivenessHostedService>();

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var value = options.Value;

		var watch = value.ExitWhenHostProcessDies ?? registrationMode.Mode == PluginRegistrationMode.Managed;
		if (!watch)
		{
			return;
		}

		if (!HostProcessTarget.TryParse(value.HostProcessId, value.HostStartedAt, out var target))
		{
			return;
		}

		using var timer = new PeriodicTimer(PollInterval, timeProvider);

		while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
		{
			if (!target.IsAlive())
			{
				_logger.HostProcessGone(target.ProcessId);
				lifetime.StopApplication();
				return;
			}
		}
	}
}
