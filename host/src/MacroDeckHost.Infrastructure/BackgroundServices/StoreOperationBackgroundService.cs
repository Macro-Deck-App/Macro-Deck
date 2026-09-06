using MacroDeckHost.Application.Store;
using MacroDeckHost.Application.Store.Operations;
using MacroDeckHost.Infrastructure.Store;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

/// <summary>Drains queued store installs and updates one at a time. Plugin activation, icon import and
/// profile import all mutate shared in-memory caches (the plugin runtime table, the icon pack cache, the
/// profile cache), so installs are serialised deliberately through a single worker rather than run
/// concurrently - two installs touching those caches at once would race.</summary>
public sealed class StoreOperationBackgroundService : HostReadyBackgroundService
{
	private readonly StoreOperationChannel _channel;
	private readonly StoreOperationCancellation _cancellation;
	private readonly IStoreOperationStore _operationStore;
	private readonly IStoreOperationTracker _tracker;
	private readonly IStoreInstallExecutor _executor;
	private readonly ILogger _logger;

	public StoreOperationBackgroundService(IHostApplicationLifetime lifetime,
		StoreOperationChannel channel,
		StoreOperationCancellation cancellation,
		IStoreOperationStore operationStore,
		IStoreOperationTracker tracker,
		IStoreInstallExecutor executor,
		ILogger logger)
		: base(lifetime)
	{
		_channel = channel;
		_cancellation = cancellation;
		_operationStore = operationStore;
		_tracker = tracker;
		_executor = executor;
		_logger = logger.ForContext<StoreOperationBackgroundService>();
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		// An operation the host was running when it stopped cannot be resumed - the process that owned
		// it is gone - so it comes back as a failure the user can retry rather than as an install that
		// silently never happened.
		_tracker.Restore(_operationStore.LoadAll());

		await foreach (var operationId in _channel.Reader.ReadAllAsync(stoppingToken))
		{
			var operation = _tracker.Find(operationId);
			if (operation is null || operation.IsTerminal)
			{
				continue;
			}

			var cts = _cancellation.Begin(operationId, stoppingToken);
			try
			{
				await _executor.Execute(operationId, cts.Token);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Store operation {OperationId} worker threw", operationId);
			}
			finally
			{
				_cancellation.End(operationId);
			}
		}
	}
}
