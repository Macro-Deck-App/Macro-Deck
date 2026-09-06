namespace MacroDeckHost.Application.Rendering;

public sealed record WidgetOptimisticStateIdentity(
	Guid WidgetId,
	string BlockId,
	string IntegrationId,
	string ActionId);

public sealed record WidgetOptimisticState(string ExpectedStateId, DateTimeOffset ExpiresAt, long Generation);

public sealed record WidgetOptimisticStateVersion(WidgetOptimisticStateIdentity Identity, long Revision);

public sealed record WidgetOptimisticStateSnapshot(
	WidgetOptimisticState? State,
	WidgetOptimisticStateVersion Version);

public sealed class WidgetOptimisticStateStore
{
	private static readonly TimeSpan _lifetime = TimeSpan.FromSeconds(5);
	private readonly object _gate = new();
	private readonly Dictionary<WidgetOptimisticStateIdentity, long> _generations = [];
	private readonly Dictionary<WidgetOptimisticStateIdentity, Entry> _entries = [];
	private readonly Dictionary<WidgetOptimisticStateIdentity, long> _revisions = [];
	private readonly TimeProvider _timeProvider;
	private readonly WidgetStateEvalChannel? _stateEvalQueue;
	private long _nextGeneration;
	private long _nextRevision;

	public WidgetOptimisticStateStore(TimeProvider timeProvider, WidgetStateEvalChannel? stateEvalQueue = null)
	{
		_timeProvider = timeProvider;
		_stateEvalQueue = stateEvalQueue;
	}

	public long Begin(WidgetOptimisticStateIdentity identity)
	{
		lock (_gate)
		{
			var generation = ++_nextGeneration;
			_generations[identity] = generation;
			Touch(identity);
			return generation;
		}
	}

	public bool TryApply(WidgetOptimisticStateIdentity identity, long generation, string expectedStateId)
	{
		if (string.IsNullOrWhiteSpace(expectedStateId))
		{
			return false;
		}

		DateTimeOffset expiresAt;
		lock (_gate)
		{
			if (_generations.GetValueOrDefault(identity) != generation)
			{
				return false;
			}

			expiresAt = _timeProvider.GetUtcNow() + _lifetime;
			_entries[identity] = new Entry(expectedStateId, expiresAt, generation);
			Touch(identity);
		}

		_ = ExpireAfter(identity, generation, expiresAt);
		return true;
	}

	public WidgetOptimisticState? Get(WidgetOptimisticStateIdentity identity)
		=> Read(identity).State;

	public WidgetOptimisticStateSnapshot Read(WidgetOptimisticStateIdentity identity)
	{
		WidgetOptimisticStateSnapshot snapshot;
		var expired = false;
		lock (_gate)
		{
			if (!_entries.TryGetValue(identity, out var entry))
			{
				return Snapshot(identity, null);
			}

			if (entry.ExpiresAt <= _timeProvider.GetUtcNow())
			{
				_entries.Remove(identity);
				Touch(identity);
				expired = true;
				snapshot = Snapshot(identity, null);
			}
			else
			{
				snapshot = Snapshot(identity, entry);
			}
		}

		if (expired)
		{
			_stateEvalQueue?.Enqueue(identity.WidgetId);
		}

		return snapshot;
	}

	public WidgetOptimisticStateVersion Observe(WidgetOptimisticStateIdentity identity)
		=> Read(identity).Version;

	public bool IsCurrent(WidgetOptimisticStateVersion version)
	{
		lock (_gate)
		{
			return _revisions.GetValueOrDefault(version.Identity) == version.Revision;
		}
	}

	public WidgetOptimisticStateSnapshot Confirm(
		WidgetOptimisticStateIdentity identity,
		long generation,
		string activeStateId)
	{
		lock (_gate)
		{
			if (_entries.TryGetValue(identity, out var entry) &&
				entry.Generation == generation &&
				entry.ExpectedStateId == activeStateId)
			{
				_entries.Remove(identity);
				Touch(identity);
			}

			return Snapshot(identity, _entries.GetValueOrDefault(identity));
		}
	}

	public WidgetOptimisticStateSnapshot Remove(WidgetOptimisticStateIdentity identity, long generation)
	{
		lock (_gate)
		{
			if (_entries.TryGetValue(identity, out var entry) && entry.Generation == generation)
			{
				_entries.Remove(identity);
				Touch(identity);
			}

			return Snapshot(identity, _entries.GetValueOrDefault(identity));
		}
	}

	public void ClearWidget(Guid widgetId)
	{
		lock (_gate)
		{
			var identities = _entries.Keys
				.Concat(_generations.Keys)
				.Concat(_revisions.Keys)
				.Where(key => key.WidgetId == widgetId)
				.Distinct()
				.ToList();
			foreach (var identity in identities)
			{
				_entries.Remove(identity);
				_generations.Remove(identity);
				_revisions.Remove(identity);
			}
		}
	}

	public IReadOnlyList<Guid> Expire()
	{
		lock (_gate)
		{
			var now = _timeProvider.GetUtcNow();
			var expired = _entries.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToList();
			foreach (var identity in expired)
			{
				_entries.Remove(identity);
				Touch(identity);
			}

			return expired.Select(identity => identity.WidgetId).Distinct().ToList();
		}
	}

	private async Task ExpireAfter(
		WidgetOptimisticStateIdentity identity,
		long generation,
		DateTimeOffset expiresAt)
	{
		await Task.Delay(_lifetime, _timeProvider, CancellationToken.None);

		var expired = false;
		lock (_gate)
		{
			if (_entries.TryGetValue(identity, out var entry) &&
				entry.Generation == generation &&
				entry.ExpiresAt == expiresAt &&
				entry.ExpiresAt <= _timeProvider.GetUtcNow())
			{
				_entries.Remove(identity);
				Touch(identity);
				expired = true;
			}
		}

		if (expired)
		{
			_stateEvalQueue?.Enqueue(identity.WidgetId);
		}
	}

	private void Touch(WidgetOptimisticStateIdentity identity)
		=> _revisions[identity] = ++_nextRevision;

	private WidgetOptimisticStateSnapshot Snapshot(WidgetOptimisticStateIdentity identity, Entry? entry)
		=> new(entry is null
				? null
				: new WidgetOptimisticState(entry.ExpectedStateId, entry.ExpiresAt, entry.Generation),
			new WidgetOptimisticStateVersion(identity, _revisions.GetValueOrDefault(identity)));

	private sealed record Entry(string ExpectedStateId, DateTimeOffset ExpiresAt, long Generation);
}
