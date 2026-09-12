using System.Text.Json;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Ui.Model.Identity;
using MacroDeck.Ui.Model.Surfaces;

namespace MacroDeckHost.Application.Ui.Sessions;

public enum UiSessionState
{
	Opening,

	Open,

	Draining,

	Closed,

	Invalidated
}

public enum UiSessionEndReason
{
	Closed,

	Drained,

	Invalidated
}

public sealed class UiSessionEndedEventArgs : EventArgs
{
	public required UiSessionSnapshot Session { get; init; }

	public required UiSessionEndReason Reason { get; init; }

	public string? Code { get; init; }

	public string? Message { get; init; }

	public bool Retryable { get; init; }
}

public sealed record UiSessionSnapshot
{
	public required string SessionId { get; init; }

	public required string ProviderId { get; init; }

	public required UiSurface Surface { get; init; }

	public required string SessionMode { get; init; }

	public required string OwnerPrincipal { get; init; }

	public required UiSessionState State { get; init; }

	public required int Revision { get; init; }

	public required IReadOnlyList<string> Attachments { get; init; }
}

public readonly record struct UiSessionCreateOutcome
{
	public required bool Accepted { get; init; }

	public string? SessionId { get; init; }

	public string? Code { get; init; }

	public string? Message { get; init; }
}

public readonly record struct UiSessionAttachOutcome
{
	public required bool Accepted { get; init; }

	public string? Code { get; init; }

	public string? Message { get; init; }

	public int Revision { get; init; }

	public string SurfaceKind { get; init; }

	public string SessionMode { get; init; }
}

public readonly record struct UiSessionDetachOutcome
{
	public required bool Detached { get; init; }
}

public enum UiRelayAction
{
	Deliver,

	Resync,

	Reject,

	Terminate,

	Ignore
}

public readonly record struct UiRelayDecision
{
	public required UiRelayAction Action { get; init; }

	public string? Code { get; init; }

	public bool Retryable { get; init; }

	public int FromRevision { get; init; }

	public int ToRevision { get; init; }

	public static UiRelayDecision Of(UiRelayAction action, string? code = null, bool retryable = false)
		=> new() { Action = action, Code = code, Retryable = retryable };
}

// Deliberately holds no tree and no patch. A relay that cached the last tree would have to decide what
// to do when the provider's tree and the cache disagree, and every answer to that question is a second
// source of truth for the thing the provider is authoritative over.
public sealed class UiSessionRegistry : IDisposable
{
	private const int MaxTombstones = 256;

	private static readonly TimeSpan _drainGrace = TimeSpan.FromSeconds(15);

	// A session nobody ever attached to has no detach to arm the drain from, so without its own deadline
	// it would hold one of the provider's few slots until the provider itself went away. Longer than the
	// drain grace because it covers a client that is still on its way in, not one that has left.
	private static readonly TimeSpan _attachGrace = TimeSpan.FromSeconds(30);

	private readonly TimeProvider _timeProvider;
	private readonly object _gate = new();
	private readonly Dictionary<string, UiSessionRecord> _bySessionId = new(StringComparer.Ordinal);
	private readonly Queue<string> _endedOrder = new();
	private readonly HashSet<string> _ended = new(StringComparer.Ordinal);

	public UiSessionRegistry(TimeProvider timeProvider)
	{
		_timeProvider = timeProvider;
	}

	public event EventHandler<UiSessionEndedEventArgs>? Ended;

	public UiSessionCreateOutcome Create(string sessionId,
		string providerId,
		UiSurface surface,
		string ownerPrincipal)
	{
		ArgumentException.ThrowIfNullOrEmpty(sessionId);
		ArgumentException.ThrowIfNullOrEmpty(providerId);
		ArgumentNullException.ThrowIfNull(surface);

		var record = new UiSessionRecord
		{
			SessionId = sessionId,
			ProviderId = providerId,
			Surface = surface,
			SessionMode = NormalizeMode(surface.SessionMode),
			OwnerPrincipal = ownerPrincipal,
			CreatedAt = _timeProvider.GetUtcNow()
		};

		record.Tokens = ProtocolLimits.MaxUiUpdateBurst;
		record.LastRefillAt = record.CreatedAt;

		// A widget or preview surface is counted against its own cap: those are per widget per viewer, so a
		// provider supplying a widget type carries one per tile on screen, while every other surface is one
		// at a time. See ProtocolLimits.MaxUiWidgetSessionsPerProvider.
		var isWidgetSurface = IsWidgetSurface(surface.Kind);
		var cap = isWidgetSurface
			? ProtocolLimits.MaxUiWidgetSessionsPerProvider
			: ProtocolLimits.MaxUiSessionsPerProvider;

		lock (_gate)
		{
			var forProvider = 0;
			foreach (var candidate in _bySessionId.Values)
			{
				if (string.Equals(candidate.ProviderId, providerId, StringComparison.Ordinal) &&
					IsWidgetSurface(candidate.Surface.Kind) == isWidgetSurface)
				{
					forProvider++;
				}
			}

			if (forProvider >= cap)
			{
				return new UiSessionCreateOutcome
				{
					Accepted = false,
					Code = UiSessionErrorCodes.TooManySessions,
					Message = $"This provider already has {cap} open UI sessions."
				};
			}

			_bySessionId[record.SessionId] = record;
			ArmAttachDeadline(record);
		}

		return new UiSessionCreateOutcome { Accepted = true, SessionId = record.SessionId };
	}

	public UiSessionSnapshot? Find(string? sessionId)
	{
		if (!UiIdentifier.IsValid(sessionId))
		{
			return null;
		}

		lock (_gate)
		{
			return _bySessionId.TryGetValue(sessionId!, out var record) ? Project(record) : null;
		}
	}

	public bool MarkOpen(string sessionId)
	{
		lock (_gate)
		{
			if (!_bySessionId.TryGetValue(sessionId, out var record) || record.State != UiSessionState.Opening)
			{
				return false;
			}

			record.State = UiSessionState.Open;
			return true;
		}
	}

	public UiSessionAttachOutcome Attach(string? sessionId, string connectionId, string principal)
	{
		ArgumentException.ThrowIfNullOrEmpty(connectionId);

		if (!UiIdentifier.IsValid(sessionId))
		{
			return Rejected(UiSessionErrorCodes.SessionNotFound, "There is no session with that id.");
		}

		lock (_gate)
		{
			if (!_bySessionId.TryGetValue(sessionId!, out var record))
			{
				return Rejected(UiSessionErrorCodes.SessionNotFound, "There is no session with that id.");
			}

			// The owning principal is the control that keeps one device's half-filled dialog off another
			// device's screen. An admin scope deliberately does not bypass it: an escape hatch here would
			// make the binding advisory.
			if (!string.Equals(record.OwnerPrincipal, principal, StringComparison.Ordinal))
			{
				return Rejected(UiSessionErrorCodes.SessionForbidden,
					"This session belongs to a different principal.");
			}

			if (record.Attachments.Contains(connectionId, StringComparer.Ordinal))
			{
				return Accepted(record);
			}

			if (!string.Equals(record.SessionMode, UiSessionModes.Shared, StringComparison.Ordinal))
			{
				if (record.Attachments.Count > 0)
				{
					return Rejected(UiSessionErrorCodes.SessionBusy, "This session already has a client.");
				}
			}
			else if (record.Attachments.Count >= ProtocolLimits.MaxUiAttachmentsPerSession)
			{
				return Rejected(UiSessionErrorCodes.SessionFull, "This session has no free attachment slot.");
			}

			record.Attachments.Add(connectionId);
			record.DetachedAt = null;
			record.EverAttached = true;
			DisarmDrain(record);

			if (record.State == UiSessionState.Draining)
			{
				record.State = UiSessionState.Open;
			}

			return Accepted(record);
		}
	}

	public UiSessionDetachOutcome Detach(string? sessionId, string connectionId)
	{
		if (!UiIdentifier.IsValid(sessionId))
		{
			return new UiSessionDetachOutcome { Detached = false };
		}

		lock (_gate)
		{
			if (!_bySessionId.TryGetValue(sessionId!, out var record) ||
				!record.Attachments.Remove(connectionId))
			{
				return new UiSessionDetachOutcome { Detached = false };
			}

			if (record.Attachments.Count > 0 || record.State is UiSessionState.Closed or UiSessionState.Invalidated)
			{
				return new UiSessionDetachOutcome { Detached = true };
			}

			record.State = UiSessionState.Draining;
			record.DetachedAt = _timeProvider.GetUtcNow();
			ArmDrain(record);

			return new UiSessionDetachOutcome { Detached = true };
		}
	}

	public IReadOnlyList<string> SessionIdsFor(string connectionId)
	{
		lock (_gate)
		{
			return _bySessionId.Values
				.Where(record => record.Attachments.Contains(connectionId, StringComparer.Ordinal))
				.Select(record => record.SessionId)
				.ToList();
		}
	}

	/// <summary>
	/// The open sessions naming <paramref name="widgetId" /> as the widget they draw - a live widget
	/// surface and its ghost - and, when <paramref name="includeConfiguration" /> is set, the
	/// <c>widget-config</c> surface configuring it.
	///
	/// <para>
	/// The configuration surface is opt-in because the two callers want opposite things. A widget being
	/// deleted takes its editor with it. A widget being <i>reconfigured</i> must not: the save that
	/// reconfigured it came from that very editor, and tearing the editor down as it saves is the one
	/// thing the invalidation exists to avoid doing to a user mid-edit.
	/// </para>
	///
	/// <para>
	/// A preview never matches either way: it carries no stored widget id of its own, only an optional
	/// <see cref="UiWidgetSurfaceAttributes.VariableScopeWidgetId" /> to resolve variables against, which
	/// names a widget to read from rather than one this preview belongs to.
	/// </para>
	/// </summary>
	public IReadOnlyList<UiSessionSnapshot> SessionsForWidget(string widgetId, bool includeConfiguration = false)
	{
		lock (_gate)
		{
			return _bySessionId.Values
				.Where(record => (includeConfiguration || !IsConfigSurface(record.Surface)) &&
					string.Equals(WidgetIdOf(record.Surface), widgetId, StringComparison.Ordinal))
				.Select(Project)
				.ToList();
		}
	}

	private static bool IsConfigSurface(UiSurface surface)
		=> string.Equals(surface.Kind, UiSurfaceKinds.Config, StringComparison.Ordinal);

	private static string? WidgetIdOf(UiSurface surface)
	{
		var key = IsConfigSurface(surface)
			? UiConfigSurfaceAttributes.WidgetId
			: UiWidgetSurfaceAttributes.WidgetId;

		return surface.Attributes.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
	}

	public IReadOnlyList<UiSessionSnapshot> SessionsForProvider(string providerId)
	{
		lock (_gate)
		{
			return _bySessionId.Values
				.Where(record => string.Equals(record.ProviderId, providerId, StringComparison.Ordinal))
				.Select(Project)
				.ToList();
		}
	}

	public bool IsAttached(string? sessionId, string connectionId)
	{
		if (!UiIdentifier.IsValid(sessionId))
		{
			return false;
		}

		lock (_gate)
		{
			return _bySessionId.TryGetValue(sessionId!, out var record) &&
				record.Attachments.Contains(connectionId, StringComparer.Ordinal);
		}
	}

	public UiRelayDecision EvaluateSnapshot(string sessionId, in UiPayloadScan scan)
	{
		lock (_gate)
		{
			if (!_bySessionId.TryGetValue(sessionId, out var record) || IsTerminal(record))
			{
				return UiRelayDecision.Of(UiRelayAction.Ignore, UiSessionErrorCodes.SessionNotFound);
			}

			if (!scan.Ok)
			{
				// An oversize tree cannot be superseded by anything smaller, so the client would be left
				// on a revision that will never advance again - the frozen UI this session type exists to
				// prevent. A patch, which can be superseded, is treated far more gently.
				return scan.Code == UiSessionErrorCodes.PayloadTooLarge
					? UiRelayDecision.Of(UiRelayAction.Terminate, UiSessionErrorCodes.PayloadTooLarge)
					: UiRelayDecision.Of(UiRelayAction.Reject, scan.Code);
			}

			record.NodeCountBound = scan.CarriedNodes;
			record.LastRelayedRevision = scan.ToRevision;
			// The resync this snapshot answers has landed, but the trip that asked for it has not been
			// forgiven: only a cleanly consumed token does that, in TryConsumeUpdate. Clearing both here
			// would make the RATE_LIMITED termination below unreachable, and a provider could then force
			// a resync on every refused update forever.
			record.SnapshotPending = false;

			return new UiRelayDecision
			{
				Action = UiRelayAction.Deliver, FromRevision = scan.ToRevision, ToRevision = scan.ToRevision
			};
		}
	}

	public UiRelayDecision EvaluatePatch(string sessionId, in UiPayloadScan scan)
	{
		lock (_gate)
		{
			if (!_bySessionId.TryGetValue(sessionId, out var record) || IsTerminal(record))
			{
				return UiRelayDecision.Of(UiRelayAction.Ignore, UiSessionErrorCodes.SessionNotFound);
			}

			if (!scan.Ok)
			{
				return scan.Code == UiSessionErrorCodes.PayloadTooLarge
					? RequestResync(record, UiSessionErrorCodes.PayloadTooLarge)
					: UiRelayDecision.Of(UiRelayAction.Reject, scan.Code);
			}

			if (scan.OperationCount == 0 || scan.ToRevision <= scan.FromRevision)
			{
				return UiRelayDecision.Of(UiRelayAction.Reject, UiSessionErrorCodes.InvalidPayload);
			}

			if (scan.ToRevision <= record.LastRelayedRevision)
			{
				return UiRelayDecision.Of(UiRelayAction.Ignore);
			}

			if (scan.FromRevision != record.LastRelayedRevision)
			{
				return RequestResync(record, UiSessionErrorCodes.InvalidPayload);
			}

			// Last of the gates, so an invalid or replayed patch does not spend budget a well-behaved
			// update would have been relayed with.
			if (!TryConsumeUpdate(record))
			{
				if (record.RateTripped && !record.SnapshotPending)
				{
					return UiRelayDecision.Of(UiRelayAction.Terminate,
						UiSessionErrorCodes.RateLimited,
						retryable: true);
				}

				record.RateTripped = true;
				return RequestResync(record, UiSessionErrorCodes.RateLimited);
			}

			// Deliberately conservative: inserted nodes are counted exactly, while a remove-node is
			// credited as the single node it names, because a relay that holds no tree cannot know how
			// large the removed subtree was. The bound therefore only ever drifts upward, and drifting
			// over the cap costs a resync, which recomputes it exactly.
			var bound = record.NodeCountBound + scan.CarriedNodes - scan.RemoveOperations;
			record.NodeCountBound = bound < 0 ? 0 : bound;

			if (record.NodeCountBound > ProtocolLimits.MaxUiNodesPerTree)
			{
				return RequestResync(record, UiSessionErrorCodes.PayloadTooLarge);
			}

			record.LastRelayedRevision = scan.ToRevision;

			return new UiRelayDecision
			{
				Action = UiRelayAction.Deliver, FromRevision = scan.FromRevision, ToRevision = scan.ToRevision
			};
		}
	}

	public bool TryClose(string? sessionId, string reason)
	{
		if (!UiIdentifier.IsValid(sessionId))
		{
			return false;
		}

		UiSessionSnapshot projected;

		lock (_gate)
		{
			if (!_bySessionId.TryGetValue(sessionId!, out var record) || IsTerminal(record))
			{
				return false;
			}

			record.State = UiSessionState.Closed;
			projected = Project(record);
			Remove(record);
		}

		Ended?.Invoke(this,
			new UiSessionEndedEventArgs
			{
				Session = projected, Reason = UiSessionEndReason.Closed, Message = reason
			});

		return true;
	}

	public bool TryInvalidate(string? sessionId, string code, string message, bool retryable)
	{
		if (!UiIdentifier.IsValid(sessionId))
		{
			return false;
		}

		UiSessionSnapshot projected;

		lock (_gate)
		{
			if (!_bySessionId.TryGetValue(sessionId!, out var record) || IsTerminal(record))
			{
				return false;
			}

			record.State = UiSessionState.Invalidated;
			projected = Project(record);
			Remove(record);
		}

		Ended?.Invoke(this,
			new UiSessionEndedEventArgs
			{
				Session = projected,
				Reason = UiSessionEndReason.Invalidated,
				Code = code,
				Message = message,
				Retryable = retryable
			});

		return true;
	}

	// True for a session this host ended, for as long as the tombstone survives. It lets a late event be
	// answered with "closed" rather than "never existed", which is what a client that was attached a
	// moment ago needs to hear.
	public bool WasEnded(string? sessionId)
	{
		if (sessionId is null)
		{
			return false;
		}

		lock (_gate)
		{
			return _ended.Contains(sessionId);
		}
	}

	// Closes every session whose grace window has already elapsed. The per-session timer is
	// the primary path; this exists so a timer lost to a process pause cannot strand a session in
	// UiSessionState.Draining, or one nobody ever attached to, forever.
	public void SweepDraining()
	{
		var now = _timeProvider.GetUtcNow();
		List<string> drained;
		List<string> unattached;

		lock (_gate)
		{
			drained = _bySessionId.Values
				.Where(record => record.State == UiSessionState.Draining &&
					record.DetachedAt is { } detachedAt &&
					now - detachedAt >= _drainGrace)
				.Select(record => record.SessionId)
				.ToList();

			unattached = _bySessionId.Values
				.Where(record => !record.EverAttached &&
					!record.HeldByHost &&
					!IsTerminal(record) &&
					now - record.CreatedAt >= _attachGrace)
				.Select(record => record.SessionId)
				.ToList();
		}

		foreach (var sessionId in unattached)
		{
			CompleteUnattached(sessionId);
		}

		foreach (var sessionId in drained)
		{
			CompleteDrain(sessionId);
		}
	}

	public void Dispose()
	{
		lock (_gate)
		{
			foreach (var record in _bySessionId.Values)
			{
				DisarmDrain(record);
			}

			_bySessionId.Clear();
		}
	}

	private static bool IsWidgetSurface(string? surfaceKind)
		=> string.Equals(surfaceKind, UiSurfaceKinds.Widget, StringComparison.Ordinal) ||
			string.Equals(surfaceKind, UiSurfaceKinds.Preview, StringComparison.Ordinal);

	private static string NormalizeMode(string sessionMode)
		=> string.Equals(sessionMode, UiSessionModes.Shared, StringComparison.Ordinal)
			? UiSessionModes.Shared
			// Every other spelling, including an unrecognised one, is exclusive. The vocabulary is open
			// by design, and guessing that an unknown mode means "shared" would fan one client's tree out
			// to every other client attached to the same provider.
			: UiSessionModes.Exclusive;

	private static bool IsTerminal(UiSessionRecord record)
		=> record.State is UiSessionState.Closed or UiSessionState.Invalidated;

	private static UiSessionAttachOutcome Rejected(string code, string message)
		=> new()
		{
			Accepted = false,
			Code = code,
			Message = message,
			SurfaceKind = string.Empty,
			SessionMode = string.Empty
		};

	private static UiSessionAttachOutcome Accepted(UiSessionRecord record)
		=> new()
		{
			Accepted = true,
			Revision = record.LastRelayedRevision,
			SurfaceKind = record.Surface.Kind,
			SessionMode = record.SessionMode
		};

	private static UiSessionSnapshot Project(UiSessionRecord record)
		=> new()
		{
			SessionId = record.SessionId,
			ProviderId = record.ProviderId,
			Surface = record.Surface,
			SessionMode = record.SessionMode,
			OwnerPrincipal = record.OwnerPrincipal,
			State = record.State,
			Revision = record.LastRelayedRevision,
			Attachments = [.. record.Attachments]
		};

	private static UiRelayDecision RequestResync(UiSessionRecord record, string code)
	{
		var alreadyPending = record.SnapshotPending;
		record.SnapshotPending = true;

		return new UiRelayDecision
		{
			Action = alreadyPending ? UiRelayAction.Reject : UiRelayAction.Resync, Code = code
		};
	}

	private bool TryConsumeUpdate(UiSessionRecord record)
	{
		var now = _timeProvider.GetUtcNow();
		var elapsed = (now - record.LastRefillAt).TotalSeconds;

		if (elapsed > 0)
		{
			record.Tokens = Math.Min(ProtocolLimits.MaxUiUpdateBurst,
				record.Tokens + (elapsed * ProtocolLimits.MaxUiUpdatesPerSecond));
			record.LastRefillAt = now;
		}

		if (record.Tokens < 1)
		{
			return false;
		}

		record.Tokens -= 1;
		record.RateTripped = false;
		return true;
	}

	private void ArmDrain(UiSessionRecord record)
	{
		record.DrainTimer = _timeProvider.CreateTimer(static state => ((DrainCallback)state!).Run(),
			new DrainCallback(this, record.SessionId),
			_drainGrace,
			Timeout.InfiniteTimeSpan);
	}

	private void ArmAttachDeadline(UiSessionRecord record)
	{
		record.DrainTimer = _timeProvider.CreateTimer(static state => ((AttachDeadlineCallback)state!).Run(),
			new AttachDeadlineCallback(this, record.SessionId),
			_attachGrace,
			Timeout.InfiniteTimeSpan);
	}

	private static void DisarmDrain(UiSessionRecord record)
	{
		record.DrainTimer?.Dispose();
		record.DrainTimer = null;
	}

	private void Remove(UiSessionRecord record)
	{
		DisarmDrain(record);
		_bySessionId.Remove(record.SessionId);

		if (_ended.Add(record.SessionId))
		{
			_endedOrder.Enqueue(record.SessionId);
		}

		while (_endedOrder.Count > MaxTombstones)
		{
			_ended.Remove(_endedOrder.Dequeue());
		}
	}

	private void CompleteDrain(string sessionId)
	{
		UiSessionSnapshot projected;

		lock (_gate)
		{
			if (!_bySessionId.TryGetValue(sessionId, out var record) || record.State != UiSessionState.Draining)
			{
				return;
			}

			record.State = UiSessionState.Closed;
			projected = Project(record);
			Remove(record);
		}

		Ended?.Invoke(this,
			new UiSessionEndedEventArgs
			{
				Session = projected,
				Reason = UiSessionEndReason.Drained,
				Message = "The last client detached and the grace window elapsed."
			});
	}

	private void CompleteUnattached(string sessionId)
	{
		UiSessionSnapshot projected;

		lock (_gate)
		{
			if (!_bySessionId.TryGetValue(sessionId, out var record) ||
				record.EverAttached ||
				record.HeldByHost ||
				IsTerminal(record))
			{
				return;
			}

			record.State = UiSessionState.Closed;
			projected = Project(record);
			Remove(record);
		}

		Ended?.Invoke(this,
			new UiSessionEndedEventArgs
			{
				Session = projected,
				Reason = UiSessionEndReason.Drained,
				Message = "No client attached within the grace window."
			});
	}

	// A session the host reads itself has no client to wait for, and lives until the host closes it.
	public bool HoldForHost(string sessionId)
	{
		lock (_gate)
		{
			if (!_bySessionId.TryGetValue(sessionId, out var record) || IsTerminal(record))
			{
				return false;
			}

			record.HeldByHost = true;
			if (!record.EverAttached)
			{
				DisarmDrain(record);
			}

			return true;
		}
	}

	private sealed class AttachDeadlineCallback
	{
		private readonly UiSessionRegistry _registry;
		private readonly string _sessionId;

		public AttachDeadlineCallback(UiSessionRegistry registry, string sessionId)
		{
			_registry = registry;
			_sessionId = sessionId;
		}

		public void Run() => _registry.CompleteUnattached(_sessionId);
	}

	private sealed class DrainCallback
	{
		private readonly UiSessionRegistry _registry;
		private readonly string _sessionId;

		public DrainCallback(UiSessionRegistry registry, string sessionId)
		{
			_registry = registry;
			_sessionId = sessionId;
		}

		public void Run() => _registry.CompleteDrain(_sessionId);
	}

	private sealed class UiSessionRecord
	{
		public required string SessionId { get; init; }

		public required string ProviderId { get; init; }

		public required UiSurface Surface { get; init; }

		public required string SessionMode { get; init; }

		public required string OwnerPrincipal { get; init; }

		public required DateTimeOffset CreatedAt { get; init; }

		public UiSessionState State { get; set; } = UiSessionState.Opening;

		public List<string> Attachments { get; } = [];

		public int LastRelayedRevision { get; set; }

		public int NodeCountBound { get; set; }

		public bool SnapshotPending { get; set; }

		public bool RateTripped { get; set; }

		public double Tokens { get; set; }

		public DateTimeOffset LastRefillAt { get; set; }

		public DateTimeOffset? DetachedAt { get; set; }

		public bool EverAttached { get; set; }

		public bool HeldByHost { get; set; }

		public ITimer? DrainTimer { get; set; }
	}
}
