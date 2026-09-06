using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Domain.Enums;
using Mediator;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Icons;

public sealed class IconImportBatchFinalizer
{
	private readonly IIconPackCache _iconPackCache;
	private readonly IconImportBatchTracker _batchTracker;
	private readonly IIconStorage _storage;
	private readonly ILogger _logger;

	public IconImportBatchFinalizer(
		IIconPackCache iconPackCache,
		IconImportBatchTracker batchTracker,
		IIconStorage storage,
		ILogger logger)
	{
		_iconPackCache = iconPackCache;
		_batchTracker = batchTracker;
		_storage = storage;
		_logger = logger;
	}

	public async Task<bool> TryFinalize(Guid batchId, IMediator mediator, CancellationToken cancellationToken)
	{
		if (!_batchTracker.IsFinished(batchId))
		{
			return false;
		}

		var (total, processed, failed) = _batchTracker.GetCounters(batchId);
		var batch = _batchTracker.TryFinish(batchId);
		if (batch is null)
		{
			return false;
		}

		batch.State = failed == 0 ? IconImportBatchState.Completed : IconImportBatchState.CompletedWithErrors;
		batch.Total = total;

		await _iconPackCache.FlushPendingWrites();
		await mediator.Publish(new IconImportProgressNotification(batch, total, processed, failed),
			cancellationToken);
		_storage.CleanupBatchStaging(batchId);

		_logger.Information("Icon import batch {BatchId} finished: {Processed} processed, {Failed} failed",
			batchId,
			processed,
			failed);
		return true;
	}
}
