using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Notifications;

namespace MacroDeckHost.Infrastructure.Notifications;

public sealed class UserNotificationStore : IUserNotificationStore
{
	public const int DefaultCapacity = 100;

	private const int MaxTitleLength = 200;
	private const int MaxMessageLength = 1000;
	private const int MaxActions = 3;

	private readonly object _lock = new();
	private readonly List<UserNotification> _entries = [];
	private readonly Dictionary<string, string> _idByDedupeKey = new(StringComparer.Ordinal);

	// Keys whose entry was retired by its own producer. Kept so RaiseIfAbsent still refuses them:
	// the dedupe mapping is the only thing stopping a straggling progress tick from re-creating an
	// entry the batch already finished with, and Retire has to drop that mapping to remove the
	// entry. Bounded like _entries, oldest first, since nothing else ages a reservation out.
	//
	// Each reservation carries a stamp and the queue carries it too, so a key that was un-reserved
	// and retired again is not evicted early by the queue slot its first reservation left behind.
	private readonly Dictionary<string, long> _retiredKeys = new(StringComparer.Ordinal);
	private readonly Queue<(string Key, long Stamp)> _retiredKeyOrder = new();
	private readonly int _capacity;

	private long _sequence;
	private long _retireStamp;

	public UserNotificationStore(int capacity = DefaultCapacity)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
		_capacity = capacity;
	}

	public int Capacity => _capacity;

	public event Action? Changed;

	public UserNotification? Raise(UserNotificationDraft draft) => RaiseCore(draft, onlyIfAbsent: false);

	public UserNotification? RaiseIfAbsent(UserNotificationDraft draft) => RaiseCore(draft, onlyIfAbsent: true);

	private UserNotification? RaiseCore(UserNotificationDraft draft, bool onlyIfAbsent)
	{
		if (draft is null || string.IsNullOrWhiteSpace(draft.Title))
		{
			return null;
		}

		var title = Clamp(LogRedactor.Redact(draft.Title), MaxTitleLength);
		var message = draft.Message is null ? null : Clamp(LogRedactor.Redact(draft.Message), MaxMessageLength);

		// Actions wins when non-empty; otherwise the singular Action, when present, is lifted into a
		// one-element list. Action is then set back to the list's first entry, so it stays a permanent
		// mirror and every consumer that reads only the singular field keeps working unchanged.
		var actions = draft.Actions.Count > 0
			? draft.Actions.Take(MaxActions).ToList()
			: draft.Action is { } soleAction
				? [soleAction]
				: [];
		var action = actions.Count > 0 ? actions[0] : null;

		UserNotification entry;
		lock (_lock)
		{
			var now = DateTimeOffset.Now;
			var dedupeKey = draft.DedupeKey;
			var hasExisting = !string.IsNullOrEmpty(dedupeKey) &&
				(_idByDedupeKey.ContainsKey(dedupeKey) || _retiredKeys.ContainsKey(dedupeKey));

			if (onlyIfAbsent && hasExisting)
			{
				return null;
			}

			// An explicit Raise on a retired key is the producer deliberately speaking again (an
			// outcome after the running entry was retired), so the key goes back into circulation.
			if (!string.IsNullOrEmpty(dedupeKey))
			{
				_retiredKeys.Remove(dedupeKey);
			}

			if (!string.IsNullOrEmpty(dedupeKey) && _idByDedupeKey.TryGetValue(dedupeKey, out var existingId))
			{
				var index = _entries.FindIndex(e => e.Id == existingId);
				if (index >= 0)
				{
					_entries.RemoveAt(index);
				}

				entry = new UserNotification
				{
					Id = existingId,
					Sequence = ++_sequence,
					Timestamp = now,
					Severity = draft.Severity,
					Kind = draft.Kind,
					Title = title,
					Message = message,
					SourceId = draft.SourceId,
					SourceName = draft.SourceName,
					Action = action,
					Actions = actions,
					Progress = draft.Progress,
					CancelKey = draft.CancelKey
				};
			}
			else
			{
				entry = new UserNotification
				{
					Id = Guid.NewGuid().ToString(),
					Sequence = ++_sequence,
					Timestamp = now,
					Severity = draft.Severity,
					Kind = draft.Kind,
					Title = title,
					Message = message,
					SourceId = draft.SourceId,
					SourceName = draft.SourceName,
					Action = action,
					Actions = actions,
					Progress = draft.Progress,
					CancelKey = draft.CancelKey
				};
			}

			_entries.Insert(0, entry);
			if (!string.IsNullOrEmpty(dedupeKey))
			{
				_idByDedupeKey[dedupeKey] = entry.Id;
			}

			TrimToCapacity();
		}

		RaiseChanged();
		return entry;
	}

	public IReadOnlyList<UserNotification> Snapshot()
	{
		lock (_lock)
		{
			return _entries.ToList();
		}
	}

	public bool Dismiss(string id)
	{
		bool removed;
		lock (_lock)
		{
			var index = _entries.FindIndex(e => e.Id == id);
			removed = index >= 0 && _entries[index].Progress is null;
			if (removed)
			{
				_entries.RemoveAt(index);
				RemoveDedupeMapping(id);
			}
		}

		if (removed)
		{
			RaiseChanged();
		}

		return removed;
	}

	public bool DismissByKey(string dedupeKey)
	{
		bool removed;
		lock (_lock)
		{
			if (!_idByDedupeKey.TryGetValue(dedupeKey, out var id))
			{
				return false;
			}

			var index = _entries.FindIndex(e => e.Id == id);
			removed = index >= 0;
			if (removed)
			{
				_entries.RemoveAt(index);
			}

			_idByDedupeKey.Remove(dedupeKey);
		}

		if (removed)
		{
			RaiseChanged();
		}

		return removed;
	}

	public bool Retire(string dedupeKey)
	{
		bool removed;
		lock (_lock)
		{
			// Reserved even when there is nothing to remove, and that case is the important one: a
			// producer whose work ended before its first progress tick was published retires a key
			// it never raised, and the reservation is all that stops that tick from announcing an
			// import that is already over - as an entry nobody could then dismiss.
			RememberRetired(dedupeKey);

			removed = false;
			if (_idByDedupeKey.TryGetValue(dedupeKey, out var id))
			{
				var index = _entries.FindIndex(e => e.Id == id);
				removed = index >= 0;
				if (removed)
				{
					_entries.RemoveAt(index);
				}

				_idByDedupeKey.Remove(dedupeKey);
			}
		}

		if (removed)
		{
			RaiseChanged();
		}

		return removed;
	}

	public bool DismissAll()
	{
		bool removedAny;
		lock (_lock)
		{
			var kept = _entries.Where(e => e.Progress is not null).ToList();
			removedAny = kept.Count < _entries.Count;
			_entries.Clear();
			_entries.AddRange(kept);

			var keptIds = kept.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
			foreach (var key in _idByDedupeKey.Where(p => !keptIds.Contains(p.Value)).Select(p => p.Key).ToList())
			{
				_idByDedupeKey.Remove(key);
			}
		}

		if (removedAny)
		{
			RaiseChanged();
		}

		return removedAny;
	}

	public bool UpdateProgress(string dedupeKey, UserNotificationProgress progress)
	{
		bool changed;
		lock (_lock)
		{
			changed = false;
			if (!string.IsNullOrEmpty(dedupeKey) && _idByDedupeKey.TryGetValue(dedupeKey, out var id))
			{
				var index = _entries.FindIndex(e => e.Id == id);
				if (index >= 0 && _entries[index].Progress is not null && !Equals(_entries[index].Progress, progress))
				{
					// Replace only Progress, at the same index - unlike Raise's dedupe replacement,
					// this must not re-timestamp the entry or move it to the front.
					_entries[index] = _entries[index] with { Progress = progress };
					changed = true;
				}
			}
		}

		if (changed)
		{
			RaiseChanged();
		}

		return changed;
	}

	private void RememberRetired(string dedupeKey)
	{
		var stamp = ++_retireStamp;
		_retiredKeys[dedupeKey] = stamp;
		_retiredKeyOrder.Enqueue((dedupeKey, stamp));

		while (_retiredKeyOrder.Count > _capacity)
		{
			var (key, retiredAt) = _retiredKeyOrder.Dequeue();
			if (_retiredKeys.TryGetValue(key, out var current) && current == retiredAt)
			{
				_retiredKeys.Remove(key);
			}
		}
	}

	private void TrimToCapacity()
	{
		while (_entries.Count > _capacity)
		{
			var last = _entries[^1];
			_entries.RemoveAt(_entries.Count - 1);
			RemoveDedupeMapping(last.Id);
		}
	}

	private void RemoveDedupeMapping(string id)
	{
		string? keyToRemove = null;
		foreach (var pair in _idByDedupeKey)
		{
			if (pair.Value == id)
			{
				keyToRemove = pair.Key;
				break;
			}
		}

		if (keyToRemove is not null)
		{
			_idByDedupeKey.Remove(keyToRemove);
		}
	}

	private static string Clamp(string value, int maxLength)
		=> value.Length <= maxLength ? value : value[..maxLength];

	private void RaiseChanged()
	{
		try
		{
			Changed?.Invoke();
		}
		catch (Exception)
		{
		}
	}
}
