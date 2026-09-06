using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public abstract class HostReadyBackgroundService : BackgroundService
{
	private readonly IHostApplicationLifetime _lifetime;
	private readonly ILogger _logger;

	protected HostReadyBackgroundService(IHostApplicationLifetime lifetime)
	{
		_lifetime = lifetime;
		_logger = Log.ForContext(GetType());
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		await WaitForHostReady(stoppingToken);
		_logger.Information("Host ready, starting execution of {ServiceName}", GetType().Name);
		await ExecuteWhenReady(stoppingToken);
	}

	protected abstract Task ExecuteWhenReady(CancellationToken stoppingToken);

	private Task WaitForHostReady(CancellationToken stoppingToken)
	{
		var tcs = new TaskCompletionSource();

		_lifetime.ApplicationStarted.Register(() => tcs.TrySetResult());
		stoppingToken.Register(() => tcs.TrySetCanceled());

		return tcs.Task;
	}
}
