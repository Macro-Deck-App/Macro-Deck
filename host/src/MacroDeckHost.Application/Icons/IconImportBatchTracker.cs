using System.Collections.Concurrent;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Icons;

public sealed class IconImportBatchTracker
{
	private sealed class TrackedBatch
	{
		public required IconImportBatchEntity Batch { get; init; }
		public int Total;
		public int Processed;
		public int Failed;
		public int AppearanceTotal;
		public int AppearanceProcessed;
		public int AppearanceFailed;
		public long LastProgressEmitTicks;
	}

	private static readonly TimeSpan _progressEmitInterval = TimeSpan.FromMilliseconds(250);

	private readonly IIconImportBatchStore _store;
	private readonly ConcurrentDictionary<Guid, TrackedBatch> _batches = new();

	public IconImportBatchTracker(IIconImportBatchStore store)
	{
		_store = store;
	}

	public void Register(IconImportBatchEntity batch, int knownTotal = 0)
	{
		_batches[batch.Id] = new TrackedBatch { Batch = batch, Total = knownTotal };
		Persist(batch);
	}

	public IconImportBatchEntity? Get(Guid batchId)
		=> _batches.TryGetValue(batchId, out var tracked) ? tracked.Batch : null;

	public List<IconImportBatchEntity> GetAll() => _batches.Values.Select(t => t.Batch).ToList();

	public (int Total, int Processed, int Failed) GetCounters(Guid batchId)
		=> _batches.TryGetValue(batchId, out var tracked)
			? (Volatile.Read(ref tracked.Total) - Volatile.Read(ref tracked.AppearanceTotal),
				Volatile.Read(ref tracked.Processed) - Volatile.Read(ref tracked.AppearanceProcessed),
				Volatile.Read(ref tracked.Failed) - Volatile.Read(ref tracked.AppearanceFailed))
			: (0, 0, 0);

	public void SetCounters(Guid batchId,
		(int Total, int Processed, int Failed) icons,
		(int Total, int Processed, int Failed) appearances)
	{
		if (_batches.TryGetValue(batchId, out var tracked))
		{
			tracked.Total = icons.Total + appearances.Total;
			tracked.Processed = icons.Processed + appearances.Processed;
			tracked.Failed = icons.Failed + appearances.Failed;
			tracked.AppearanceTotal = appearances.Total;
			tracked.AppearanceProcessed = appearances.Processed;
			tracked.AppearanceFailed = appearances.Failed;
		}
	}

	public void AddToTotal(Guid batchId, int count)
	{
		if (_batches.TryGetValue(batchId, out var tracked))
		{
			Interlocked.Add(ref tracked.Total, count);
		}
	}

	public void AddToTotal(Guid batchId, IReadOnlyCollection<IconEntity> items)
	{
		if (_batches.TryGetValue(batchId, out var tracked))
		{
			Interlocked.Add(ref tracked.AppearanceTotal, items.Count(item => item.AppearanceOfId is not null));
			Interlocked.Add(ref tracked.Total, items.Count);
		}
	}

	public void IncrementProcessed(Guid batchId, bool appearance = false)
	{
		if (_batches.TryGetValue(batchId, out var tracked))
		{
			if (appearance)
			{
				Interlocked.Increment(ref tracked.AppearanceProcessed);
			}

			Interlocked.Increment(ref tracked.Processed);
		}
	}

	public void AddProcessed(Guid batchId, int count)
	{
		if (_batches.TryGetValue(batchId, out var tracked))
		{
			Interlocked.Add(ref tracked.Processed, count);
		}
	}

	public void IncrementFailed(Guid batchId, bool appearance = false)
	{
		if (_batches.TryGetValue(batchId, out var tracked))
		{
			if (appearance)
			{
				Interlocked.Increment(ref tracked.AppearanceFailed);
			}

			Interlocked.Increment(ref tracked.Failed);
		}
	}

	public bool HasItems(Guid batchId)
		=> _batches.TryGetValue(batchId, out var tracked) && Volatile.Read(ref tracked.Total) > 0;

	public bool IsFinished(Guid batchId)
	{
		if (!_batches.TryGetValue(batchId, out var tracked))
		{
			return true;
		}

		return tracked.Batch.State is not IconImportBatchState.Discovering &&
			Volatile.Read(ref tracked.Processed) + Volatile.Read(ref tracked.Failed) >=
			Volatile.Read(ref tracked.Total);
	}

	public bool ShouldEmitProgress(Guid batchId, bool force)
	{
		if (!_batches.TryGetValue(batchId, out var tracked))
		{
			return false;
		}

		var now = DateTime.UtcNow.Ticks;
		var last = Volatile.Read(ref tracked.LastProgressEmitTicks);
		if (!force && now - last < _progressEmitInterval.Ticks)
		{
			return false;
		}

		return Interlocked.CompareExchange(ref tracked.LastProgressEmitTicks, now, last) == last || force;
	}

	public void Persist(IconImportBatchEntity batch)
	{
		batch.UpdatedAt = DateTime.UtcNow;
		_store.Save(ToFile(batch));
	}

	public IconImportBatchEntity? TryFinish(Guid batchId)
	{
		if (!_batches.TryRemove(batchId, out var tracked))
		{
			return null;
		}

		_store.Delete(batchId);
		return tracked.Batch;
	}

	public IReadOnlyList<IconImportBatchEntity> LoadPersisted()
		=> _store.LoadAll().Select(ToEntity).ToList();

	private static Persistence.Icons.IconImportBatchFile ToFile(IconImportBatchEntity batch)
		=> new()
		{
			Id = batch.Id,
			PackId = batch.PackId,
			SourceName = batch.SourceName,
			State = batch.State,
			Total = batch.Total,
			Error = batch.Error,
			Skipped = batch.Skipped,
			ExplicitDestination = batch.ExplicitDestination,
			Mode = batch.Mode,
			Silent = batch.Silent,
			CreatedAt = batch.CreatedAt,
			UpdatedAt = batch.UpdatedAt
		};

	private static IconImportBatchEntity ToEntity(Persistence.Icons.IconImportBatchFile file)
		=> new()
		{
			Id = file.Id,
			PackId = file.PackId,
			SourceName = file.SourceName,
			State = file.State,
			Total = file.Total,
			Error = file.Error,
			Skipped = file.Skipped,
			ExplicitDestination = file.ExplicitDestination,
			Mode = file.Mode,
			Silent = file.Silent,
			CreatedAt = file.CreatedAt,
			UpdatedAt = file.UpdatedAt
		};
}
