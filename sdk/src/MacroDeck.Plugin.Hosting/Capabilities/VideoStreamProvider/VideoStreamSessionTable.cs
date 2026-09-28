namespace MacroDeck.Plugin.Hosting.Capabilities.VideoStreamProvider;

// Kept free of anything beyond the BCL so the host can compile this file as a linked source.
internal enum VideoStreamSessionPhase
{
	Opening,
	Open,
	Closed,
}

internal enum VideoStreamOpenAdmission
{
	Admitted,
	Duplicate,
	Tombstoned,
}

internal enum VideoStreamOpenOutcome
{
	Open,
	CloseNow,
	Discard,
}

internal enum VideoStreamCloseOutcome
{
	CloseNow,
	Deferred,
	AlreadyClosed,
}

internal sealed class VideoStreamSessionTable<TReason, TProvider>
	where TReason : struct, Enum
	where TProvider : class
{
	private readonly Lock _gate = new();
	private readonly int _maxTombstones;
	private readonly Dictionary<string, Entry> _sessions = new(StringComparer.Ordinal);
	private readonly TimeProvider _time;
	private readonly TimeSpan _tombstoneLifetime;
	private readonly Queue<(string SessionId, DateTimeOffset ExpiresAt)> _tombstoneOrder = new();
	private readonly Dictionary<string, DateTimeOffset> _tombstones = new(StringComparer.Ordinal);
	private long _epoch;

	public VideoStreamSessionTable(TimeProvider time, TimeSpan tombstoneLifetime, int maxTombstones = 1024)
	{
		ArgumentNullException.ThrowIfNull(time);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(tombstoneLifetime, TimeSpan.Zero);
		ArgumentOutOfRangeException.ThrowIfLessThan(maxTombstones, 1);
		_time = time;
		_tombstoneLifetime = tombstoneLifetime;
		_maxTombstones = maxTombstones;
	}

	public long CurrentEpoch => Interlocked.Read(ref _epoch);

	public long AdvanceEpoch() => Interlocked.Increment(ref _epoch);

	public VideoStreamOpenAdmission TryBeginOpen(string sessionId, string providerId, TProvider provider, long epoch)
	{
		ArgumentNullException.ThrowIfNull(provider);
		lock (_gate)
		{
			PruneTombstones();
			if (_tombstones.ContainsKey(sessionId))
			{
				return VideoStreamOpenAdmission.Tombstoned;
			}

			if (!_sessions.TryAdd(sessionId, new Entry(sessionId, providerId, provider, epoch)))
			{
				return VideoStreamOpenAdmission.Duplicate;
			}

			return VideoStreamOpenAdmission.Admitted;
		}
	}

	public OpenCompletion CompleteOpen(string sessionId)
	{
		lock (_gate)
		{
			if (!_sessions.TryGetValue(sessionId, out var entry) || entry.Phase != VideoStreamSessionPhase.Opening)
			{
				return new OpenCompletion(VideoStreamOpenOutcome.Discard, null);
			}

			if (entry.DeferredReason is { } reason)
			{
				Retire(entry);
				return new OpenCompletion(VideoStreamOpenOutcome.CloseNow, entry.ToClosing(reason));
			}

			if (entry.EndedByProvider)
			{
				Retire(entry);
				return new OpenCompletion(VideoStreamOpenOutcome.Discard, null);
			}

			entry.Phase = VideoStreamSessionPhase.Open;
			return new OpenCompletion(VideoStreamOpenOutcome.Open, null);
		}
	}

	public void FailOpen(string sessionId)
	{
		lock (_gate)
		{
			if (_sessions.TryGetValue(sessionId, out var entry) && entry.Phase == VideoStreamSessionPhase.Opening)
			{
				Retire(entry);
			}
		}
	}

	public CloseDecision RequestClose(string sessionId, TReason reason)
	{
		lock (_gate)
		{
			PruneTombstones();
			if (!_sessions.TryGetValue(sessionId, out var entry))
			{
				if (!_tombstones.ContainsKey(sessionId))
				{
					AddTombstone(sessionId);
				}

				return new CloseDecision(VideoStreamCloseOutcome.AlreadyClosed, null);
			}

			if (entry.Phase == VideoStreamSessionPhase.Open)
			{
				Retire(entry);
				return new CloseDecision(VideoStreamCloseOutcome.CloseNow, entry.ToClosing(reason));
			}

			if (entry.IsClosing)
			{
				return new CloseDecision(VideoStreamCloseOutcome.AlreadyClosed, null);
			}

			entry.DeferredReason = reason;
			return new CloseDecision(VideoStreamCloseOutcome.Deferred, null);
		}
	}

	public bool EndByProvider(string sessionId, out string providerId)
	{
		lock (_gate)
		{
			providerId = string.Empty;
			if (!_sessions.TryGetValue(sessionId, out var entry))
			{
				return false;
			}

			providerId = entry.ProviderId;
			if (entry.Phase == VideoStreamSessionPhase.Open)
			{
				Retire(entry);
				return true;
			}

			var wasLive = !entry.IsClosing;
			entry.DeferredReason = null;
			entry.EndedByProvider = true;
			return wasLive;
		}
	}

	public bool TryGet(string sessionId, out Session session)
	{
		lock (_gate)
		{
			if (_sessions.TryGetValue(sessionId, out var entry))
			{
				session = entry.ToSession();
				return true;
			}

			session = default;
			return false;
		}
	}

	public IReadOnlyList<Closing> CloseOlderThan(long epoch, TReason reason)
		=> CloseWhere(entry => entry.Epoch < epoch, reason);

	public IReadOnlyList<Closing> CloseProvider(TProvider provider, TReason reason)
		=> CloseWhere(entry => ReferenceEquals(entry.Provider, provider), reason);

	public IReadOnlyList<Closing> CloseAll(TReason reason) => CloseWhere(_ => true, reason);

	private List<Closing> CloseWhere(Func<Entry, bool> predicate, TReason reason)
	{
		lock (_gate)
		{
			var closing = new List<Closing>();
			foreach (var entry in _sessions.Values.Where(predicate).ToList())
			{
				if (entry.Phase == VideoStreamSessionPhase.Open)
				{
					Retire(entry);
					closing.Add(entry.ToClosing(reason));
				}
				else if (!entry.IsClosing)
				{
					entry.DeferredReason = reason;
				}
			}

			return closing;
		}
	}

	private void Retire(Entry entry)
	{
		entry.Phase = VideoStreamSessionPhase.Closed;
		_sessions.Remove(entry.SessionId);
		AddTombstone(entry.SessionId);
	}

	private void AddTombstone(string sessionId)
	{
		var expiresAt = _time.GetUtcNow() + _tombstoneLifetime;
		_tombstones[sessionId] = expiresAt;
		_tombstoneOrder.Enqueue((sessionId, expiresAt));
		while (_tombstones.Count > _maxTombstones && _tombstoneOrder.TryDequeue(out var oldest))
		{
			RemoveTombstoneIfCurrent(oldest);
		}
	}

	private void PruneTombstones()
	{
		var now = _time.GetUtcNow();
		while (_tombstoneOrder.TryPeek(out var oldest) && oldest.ExpiresAt <= now)
		{
			_tombstoneOrder.Dequeue();
			RemoveTombstoneIfCurrent(oldest);
		}
	}

	private void RemoveTombstoneIfCurrent((string SessionId, DateTimeOffset ExpiresAt) tombstone)
	{
		if (_tombstones.TryGetValue(tombstone.SessionId, out var expiresAt) && expiresAt == tombstone.ExpiresAt)
		{
			_tombstones.Remove(tombstone.SessionId);
		}
	}

	internal readonly record struct Session(
		string SessionId,
		string ProviderId,
		long Epoch,
		VideoStreamSessionPhase Phase);

	internal sealed record Closing(string SessionId, string ProviderId, TProvider Provider, long Epoch, TReason Reason);

	internal readonly record struct OpenCompletion(VideoStreamOpenOutcome Outcome, Closing? Close);

	internal readonly record struct CloseDecision(VideoStreamCloseOutcome Outcome, Closing? Close);

	private sealed class Entry(string sessionId, string providerId, TProvider provider, long epoch)
	{
		public string SessionId { get; } = sessionId;

		public string ProviderId { get; } = providerId;

		public TProvider Provider { get; } = provider;

		public long Epoch { get; } = epoch;

		public VideoStreamSessionPhase Phase { get; set; } = VideoStreamSessionPhase.Opening;

		public TReason? DeferredReason { get; set; }

		public bool EndedByProvider { get; set; }

		public bool IsClosing => DeferredReason is not null || EndedByProvider;

		public Session ToSession()
			=> new(SessionId, ProviderId, Epoch, IsClosing ? VideoStreamSessionPhase.Closed : Phase);

		public Closing ToClosing(TReason reason) => new(SessionId, ProviderId, Provider, Epoch, reason);
	}
}
