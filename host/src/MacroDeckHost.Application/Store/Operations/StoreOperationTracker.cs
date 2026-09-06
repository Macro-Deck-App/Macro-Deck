using System.Collections.Concurrent;
using MacroDeckHost.Application.Store.Model;

namespace MacroDeckHost.Application.Store.Operations;

public sealed class StoreOperationTracker : IStoreOperationTracker
{
	private const int MaxRetainedTerminal = 50;
	private static readonly TimeSpan _terminalRetention = TimeSpan.FromHours(24);
	private static readonly TimeSpan _progressInterval = TimeSpan.FromMilliseconds(250);
	private static readonly TimeSpan _rateWindow = TimeSpan.FromSeconds(5);

	private readonly ConcurrentDictionary<Guid, StoreOperation> _operations = new();
	private readonly ConcurrentDictionary<Guid, RateSample> _rates = new();
	private readonly IStoreOperationStore _store;
	private readonly TimeProvider _timeProvider;

	public StoreOperationTracker(IStoreOperationStore store, TimeProvider timeProvider)
	{
		_store = store;
		_timeProvider = timeProvider;
	}

	public event Action<StoreOperation>? Changed;

	public IReadOnlyList<StoreOperation> Snapshot() =>
		_operations.Values.OrderBy(operation => operation.StartedAt).ToList();

	public StoreOperation? Find(Guid operationId) =>
		_operations.TryGetValue(operationId, out var operation) ? operation : null;

	public StoreOperation? FindLive(StoreExtensionKind kind, string packageId) =>
		_operations.Values
			.Where(operation => operation.ExtensionKind == kind &&
				string.Equals(operation.PackageId, packageId, StringComparison.OrdinalIgnoreCase) &&
				!operation.IsTerminal)
			.MaxBy(operation => operation.StartedAt);

	public StoreOperation Create(StoreOperationKind kind,
		StoreExtensionKind extensionKind,
		string packageId,
		string version,
		string displayName,
		string? previousVersion,
		Guid? retryOf = null)
	{
		var now = _timeProvider.GetUtcNow();
		var operation = new StoreOperation
		{
			Id = Guid.CreateVersion7(),
			Kind = kind,
			ExtensionKind = extensionKind,
			PackageId = packageId,
			Version = version,
			DisplayName = displayName,
			PreviousVersion = previousVersion,
			State = StoreOperationState.Queued,
			StartedAt = now,
			UpdatedAt = now,
			RetryOf = retryOf
		};

		_operations[operation.Id] = operation;
		Persist();
		Changed?.Invoke(operation);
		return operation;
	}

	public StoreOperation? Transition(Guid operationId,
		StoreOperationState state,
		StoreOperationError? failure = null,
		string? errorMessage = null)
	{
		// Retries rather than giving up when the compare-and-swap loses: a concurrent progress report must
		// never be able to swallow a transition, least of all the one that records a failure.
		while (true)
		{
			if (!_operations.TryGetValue(operationId, out var current))
			{
				return null;
			}

			// A terminal state is final. Cancellation arrives from a request while the worker is finishing
			// the same operation, so without this a cancel landing just after a completed install would
			// report a successful install as cancelled - and the reverse would bury a cancellation the
			// user asked for.
			if (current.IsTerminal)
			{
				return current;
			}

			var now = _timeProvider.GetUtcNow();
			var updated = current with
			{
				State = state,
				Error = failure,
				ErrorMessage = errorMessage,
				UpdatedAt = now,
				CompletedAt = state is StoreOperationState.Completed
					or StoreOperationState.Failed
					or StoreOperationState.Cancelled
					? now
					: current.CompletedAt,
				// Validating and installing have no measurable total, so the numbers that drove a
				// determinate bar are cleared rather than left showing a stale percentage.
				EtaSeconds = state is StoreOperationState.Downloading ? current.EtaSeconds : null
			};

			if (!_operations.TryUpdate(operationId, updated, current))
			{
				continue;
			}

			if (updated.IsTerminal)
			{
				_rates.TryRemove(operationId, out _);
				Prune();
			}

			Persist();
			Changed?.Invoke(updated);
			return updated;
		}
	}

	public void ReportProgress(Guid operationId, long bytesDownloaded, long? totalBytes)
	{
		if (!_operations.TryGetValue(operationId, out var current) || current.IsTerminal)
		{
			return;
		}

		var now = _timeProvider.GetUtcNow();
		var sample = _rates.GetOrAdd(operationId, _ => new RateSample(now, bytesDownloaded));
		var complete = totalBytes is > 0 && bytesDownloaded >= totalBytes;
		if (!complete && now - current.UpdatedAt < _progressInterval)
		{
			return;
		}

		var elapsed = now - sample.At;
		int? eta = null;
		if (totalBytes is > 0 && elapsed >= _rateWindow && bytesDownloaded > sample.Bytes)
		{
			var perSecond = (bytesDownloaded - sample.Bytes) / elapsed.TotalSeconds;
			if (perSecond > 0)
			{
				eta = (int)Math.Ceiling((totalBytes.Value - bytesDownloaded) / perSecond);
			}

			_rates[operationId] = new RateSample(now, bytesDownloaded);
		}

		var updated = current with
		{
			State = StoreOperationState.Downloading,
			BytesDownloaded = bytesDownloaded,
			TotalBytes = totalBytes,
			EtaSeconds = eta ?? current.EtaSeconds,
			UpdatedAt = now
		};

		// Compare-and-swap against the snapshot this update was computed from. Progress is reported from
		// the download loop while the worker may already have transitioned the operation to a terminal
		// state; a blind write would resurrect Downloading over a Failed and clear its error, leaving a
		// failed install showing as downloading forever.
		if (!_operations.TryUpdate(operationId, updated, current))
		{
			return;
		}

		Changed?.Invoke(updated);
	}

	public bool Dismiss(Guid operationId)
	{
		if (!_operations.TryGetValue(operationId, out var operation) || !operation.IsTerminal)
		{
			return false;
		}

		if (!_operations.TryRemove(operationId, out _))
		{
			return false;
		}

		Persist();
		return true;
	}

	// An operation the host was running when it stopped cannot be resumed, but it must not vanish either:
	// it comes back as a failure the user can retry rather than as an install that silently never happened.
	public void Restore(IReadOnlyList<StoreOperation> operations)
	{
		ArgumentNullException.ThrowIfNull(operations);

		var now = _timeProvider.GetUtcNow();
		foreach (var operation in operations)
		{
			_operations[operation.Id] = operation.IsTerminal
				? operation
				: operation with
				{
					State = StoreOperationState.Failed,
					Error = StoreOperationError.Interrupted,
					ErrorMessage = "Macro Deck stopped while this was running.",
					UpdatedAt = now,
					CompletedAt = now
				};
		}

		Prune();
		Persist();
	}

	private void Prune()
	{
		var cutoff = _timeProvider.GetUtcNow() - _terminalRetention;
		var terminal = _operations.Values
			.Where(operation => operation.IsTerminal)
			.OrderByDescending(operation => operation.CompletedAt ?? operation.UpdatedAt)
			.ToList();

		foreach (var operation in terminal.Skip(MaxRetainedTerminal)
			.Concat(terminal.Where(candidate => (candidate.CompletedAt ?? candidate.UpdatedAt) < cutoff)))
		{
			_operations.TryRemove(operation.Id, out _);
		}
	}

	private void Persist() => _store.SaveAll(Snapshot());

	private sealed record RateSample(DateTimeOffset At, long Bytes);
}
