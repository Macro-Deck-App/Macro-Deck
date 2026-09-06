using MacroDeckHost.Application.Variables;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class VariableBroadcastBackgroundService : HostReadyBackgroundService
{
	private static readonly TimeSpan _defaultWindow = TimeSpan.FromMilliseconds(200);

	private readonly VariableBroadcastChannel _queue;
	private readonly VariableBroadcaster _broadcaster;
	private readonly ILogger _logger;
	private readonly TimeSpan _window;

	public VariableBroadcastBackgroundService(
		IHostApplicationLifetime lifetime,
		VariableBroadcastChannel queue,
		VariableBroadcaster broadcaster,
		ILogger logger,
		TimeSpan? window = null)
		: base(lifetime)
	{
		_queue = queue;
		_broadcaster = broadcaster;
		_logger = logger.ForContext<VariableBroadcastBackgroundService>();
		_window = window ?? _defaultWindow;
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		var reader = _queue.Reader;
		while (await reader.WaitToReadAsync(stoppingToken))
		{
			var batch = new HashSet<Guid>();
			while (reader.TryRead(out var id))
			{
				batch.Add(id);
			}

			await Task.Delay(_window, stoppingToken);
			while (reader.TryRead(out var id))
			{
				batch.Add(id);
			}

			// A throw here would end the loop, and a stopped BackgroundService is never restarted - every
			// later variable change would be lost silently for the rest of the process.
			try
			{
				await _broadcaster.Publish(batch, stoppingToken);
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Failed to broadcast a variable change batch");
			}
		}
	}
}
