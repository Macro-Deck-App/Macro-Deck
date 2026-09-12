using System.Collections.Concurrent;
using MacroDeck.Plugin.Protocol.Auth;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Reconnection;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Plugins.Runtime;
using Serilog;

namespace MacroDeckHost.Application.Plugins;

public enum PluginSessionEndReason
{
	Detached,

	Pruned
}

public sealed class PluginSessionEndedEventArgs : EventArgs
{
	public required string PluginId { get; init; }

	public required string SessionId { get; init; }

	public required PluginSessionEndReason Reason { get; init; }
}

public sealed record PluginSessionCapabilities
{
	public required IReadOnlyList<DeclaredCapability> DeclaredCapabilities { get; init; }

	public required IReadOnlyDictionary<string, CapabilityNegotiationResult> Capabilities { get; init; }
}

public enum PluginSessionState
{
	Awaiting,

	Connected,

	Dropped
}

public sealed class PluginSessionRecord
{
	public required string SessionId { get; init; }

	public required string PluginId { get; init; }

	public required string DisplayName { get; init; }

	public Guid? AccessTokenId { get; init; }

	public required PluginSessionOrigin Origin { get; init; }

	public string? InstanceId { get; set; }

	public required int NegotiatedVersion { get; init; }

	public string? DeclaredVersion { get; init; }

	public string? DeclaredName { get; init; }

	public required IReadOnlyDictionary<string, CapabilityNegotiationResult> Capabilities { get; set; }

	public required IReadOnlyList<DeclaredCapability> DeclaredCapabilities { get; set; }

	public PluginSessionState State { get; set; }

	public required DateTimeOffset CreatedAt { get; init; }

	public DateTimeOffset? ConnectedAt { get; set; }

	public DateTimeOffset? DroppedAt { get; set; }

	public bool NonResumable { get; set; }

	public IPluginConnection? Connection { get; set; }

	internal long LastInboundTicks;

	internal TaskCompletionSource? PauseSignal;
}

public sealed record PluginSessionSnapshot
{
	public required string SessionId { get; init; }

	public required string PluginId { get; init; }

	public required string DisplayName { get; init; }

	public Guid? AccessTokenId { get; init; }

	public required PluginSessionOrigin Origin { get; init; }

	public string? InstanceId { get; init; }

	public required PluginSessionState State { get; init; }

	public required int NegotiatedVersion { get; init; }

	public string? DeclaredVersion { get; init; }

	public string? DeclaredName { get; init; }

	public required DateTimeOffset CreatedAt { get; init; }

	public DateTimeOffset? ConnectedAt { get; init; }

	public DateTimeOffset? DroppedAt { get; init; }

	public DateTimeOffset? LastInboundAt { get; init; }
}

public interface IPluginSessionRegistry
{
	Task Create(PluginSessionRecord record);

	bool TryAttach(string sessionId, IPluginConnection connection, string? instanceId);

	void Detach(string sessionId, DateTimeOffset at);

	void ReleaseConnection(string sessionId, IPluginConnection connection);

	bool TryResume(string pluginId, string? resumeSessionId, DateTimeOffset at, out PluginSessionRecord? record);

	void MakeNonResumable(string sessionId);

	void Touch(string sessionId, DateTimeOffset at);

	Task<bool> SendToPlugin(string pluginId, ProtocolEnvelope envelope, CancellationToken cancellationToken = default);

	bool IsCurrentConnection(string sessionId, IPluginConnection connection);

	Task<bool> Terminate(string sessionId, int closeCode, string reason);

	Task<bool> TerminateForPlugin(string pluginId, int closeCode, string reason);

	IReadOnlyList<PluginSessionSnapshot> Snapshot();

	void SetPaused(string sessionId, bool paused);

	PluginSessionCapabilities? GetCapabilities(string pluginId);

	/// <summary>The protocol version this plugin's current session negotiated, or <c>null</c> when it
	/// has none. Lets a wire-shape decision (e.g. the widgets host api's per-version DTOs) be made
	/// without threading the session record itself through callers that only need this one field.</summary>
	int? GetNegotiatedVersion(string pluginId);

	IReadOnlyDictionary<string, CapabilityNegotiationResult>? UpdateDeclaredCapabilities(
		string pluginId,
		IReadOnlyList<DeclaredCapability> declaredCapabilities);

	event EventHandler<PluginSessionEndedEventArgs>? SessionEnded;
}

public class PluginSessionRegistry : IPluginSessionRegistry
{
	private static readonly TimeSpan _awaitingSessionLifetime =
		PluginAuthDefaults.SessionTokenLifetime + TimeSpan.FromMinutes(1);

	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;
	private readonly object _gate = new();
	private readonly Dictionary<string, PluginSessionRecord> _bySessionId = new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _sessionIdByPluginId = new(StringComparer.Ordinal);

	private readonly ConcurrentDictionary<string, PluginSessionRecord>
		_recordsBySessionId = new(StringComparer.Ordinal);

	public PluginSessionRegistry(TimeProvider timeProvider, ILogger logger)
	{
		_timeProvider = timeProvider;
		_logger = logger.ForContext<PluginSessionRegistry>();
	}

	public event EventHandler<PluginSessionEndedEventArgs>? SessionEnded;

	public async Task Create(PluginSessionRecord record)
	{
		PluginSessionRecord? replaced = null;

		lock (_gate)
		{
			if (_sessionIdByPluginId.TryGetValue(record.PluginId, out var oldSessionId) &&
				_bySessionId.TryGetValue(oldSessionId, out replaced))
			{
				_bySessionId.Remove(oldSessionId);
				_recordsBySessionId.TryRemove(oldSessionId, out _);
			}

			_bySessionId[record.SessionId] = record;
			_sessionIdByPluginId[record.PluginId] = record.SessionId;
			_recordsBySessionId[record.SessionId] = record;
		}

		if (replaced is not null)
		{
			SessionEnded?.Invoke(this,
				new PluginSessionEndedEventArgs
				{
					PluginId = replaced.PluginId, SessionId = replaced.SessionId,
					Reason = PluginSessionEndReason.Pruned
				});
		}

		if (replaced?.Connection is { } connection)
		{
			await connection.Close(ProtocolCloseCodes.SessionReplaced, "Replaced by a new session.");
		}
	}

	public bool TryAttach(string sessionId, IPluginConnection connection, string? instanceId)
	{
		Prune();

		lock (_gate)
		{
			if (!_bySessionId.TryGetValue(sessionId, out var record))
			{
				return false;
			}

			record.Connection = connection;
			record.InstanceId = instanceId ?? record.InstanceId;
			record.State = PluginSessionState.Connected;
			record.ConnectedAt = _timeProvider.GetUtcNow();
			record.DroppedAt = null;
			return true;
		}
	}

	public void Detach(string sessionId, DateTimeOffset at)
	{
		string? pluginId = null;

		lock (_gate)
		{
			if (_bySessionId.TryGetValue(sessionId, out var record))
			{
				record.State = PluginSessionState.Dropped;
				record.DroppedAt = at;
				record.Connection = null;
				pluginId = record.PluginId;
			}
		}

		if (pluginId is not null)
		{
			SessionEnded?.Invoke(this,
				new PluginSessionEndedEventArgs
				{
					PluginId = pluginId, SessionId = sessionId, Reason = PluginSessionEndReason.Detached
				});
		}
	}

	// No SessionEnded here: goodbye is not a resumable detach. The record must survive until the plugin's
	// DELETE authenticates against it, or the resume window prunes it; either ends it with Pruned.
	public void ReleaseConnection(string sessionId, IPluginConnection connection)
	{
		lock (_gate)
		{
			if (_bySessionId.TryGetValue(sessionId, out var record) &&
				ReferenceEquals(record.Connection, connection))
			{
				record.NonResumable = true;
				record.State = PluginSessionState.Dropped;
				record.DroppedAt = _timeProvider.GetUtcNow();
				record.Connection = null;
			}
		}
	}

	public bool TryResume(string pluginId,
		string? resumeSessionId,
		DateTimeOffset at,
		out PluginSessionRecord? record)
	{
		Prune();
		record = null;

		if (!SessionResumeRules.IsResumeAttempt(resumeSessionId))
		{
			return false;
		}

		lock (_gate)
		{
			if (!_bySessionId.TryGetValue(resumeSessionId!, out var existing))
			{
				return false;
			}

			if (!string.Equals(existing.PluginId, pluginId, StringComparison.Ordinal))
			{
				return false;
			}

			var sessionExists = existing.State == PluginSessionState.Dropped && !existing.NonResumable;
			var withinWindow = existing.DroppedAt is { } droppedAt &&
				at - droppedAt <= ProtocolTimeouts.SessionResumeWindow;

			if (!SessionResumeRules.CanResume(resumeSessionId, sessionExists, withinWindow))
			{
				return false;
			}

			record = existing;
			return true;
		}
	}

	public void MakeNonResumable(string sessionId)
	{
		lock (_gate)
		{
			if (_bySessionId.TryGetValue(sessionId, out var record))
			{
				record.NonResumable = true;
			}
		}
	}

	public void Touch(string sessionId, DateTimeOffset at)
	{
		if (_recordsBySessionId.TryGetValue(sessionId, out var record))
		{
			Interlocked.Exchange(ref record.LastInboundTicks, at.UtcTicks);
		}
	}

	public async Task<bool> SendToPlugin(string pluginId,
		ProtocolEnvelope envelope,
		CancellationToken cancellationToken = default)
	{
		PluginSessionRecord? record;

		lock (_gate)
		{
			record = _sessionIdByPluginId.TryGetValue(pluginId, out var sessionId) &&
				_bySessionId.TryGetValue(sessionId, out var found)
					? found
					: null;
		}

		var connection = record?.Connection;
		if (connection is null)
		{
			PluginRuntimeLog.SendToPluginNoConnection(_logger, pluginId);
			return false;
		}

		// Reply and control types keep flowing while paused - see ProtocolBackpressure's remarks. Every
		// other type parks here until flow.resume clears the signal; re-checked after every wait, since
		// the plugin may pause again while this send was waiting.
		if (!ProtocolBackpressure.IsExemptWhilePaused(envelope.Type))
		{
			while (Volatile.Read(ref record!.PauseSignal) is { } pause)
			{
				await pause.Task.WaitAsync(cancellationToken);
			}
		}

		await connection.Send(envelope, cancellationToken);
		return true;
	}

	public void SetPaused(string sessionId, bool paused)
	{
		PluginSessionRecord? record;

		lock (_gate)
		{
			_bySessionId.TryGetValue(sessionId, out record);
		}

		if (record is null)
		{
			return;
		}

		if (paused)
		{
			Interlocked.CompareExchange(ref record.PauseSignal,
				new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
				null);
		}
		else
		{
			Interlocked.Exchange(ref record.PauseSignal, null)?.TrySetResult();
		}
	}

	public PluginSessionCapabilities? GetCapabilities(string pluginId)
	{
		lock (_gate)
		{
			if (!_sessionIdByPluginId.TryGetValue(pluginId, out var sessionId) ||
				!_bySessionId.TryGetValue(sessionId, out var record))
			{
				return null;
			}

			return new PluginSessionCapabilities
			{
				DeclaredCapabilities = record.DeclaredCapabilities, Capabilities = record.Capabilities
			};
		}
	}

	public int? GetNegotiatedVersion(string pluginId)
	{
		lock (_gate)
		{
			return _sessionIdByPluginId.TryGetValue(pluginId, out var sessionId) &&
				_bySessionId.TryGetValue(sessionId, out var record)
					? record.NegotiatedVersion
					: null;
		}
	}

	public IReadOnlyDictionary<string, CapabilityNegotiationResult>? UpdateDeclaredCapabilities(
		string pluginId,
		IReadOnlyList<DeclaredCapability> declaredCapabilities)
	{
		lock (_gate)
		{
			if (!_sessionIdByPluginId.TryGetValue(pluginId, out var sessionId) ||
				!_bySessionId.TryGetValue(sessionId, out var record))
			{
				return null;
			}

			var negotiated = declaredCapabilities
				.Select(capability => CapabilityVersionNegotiator.Negotiate(capability.Kind, capability.VersionRange))
				.ToDictionary(result => result.Kind, StringComparer.Ordinal);

			record.DeclaredCapabilities = declaredCapabilities;
			record.Capabilities = negotiated;
			return negotiated;
		}
	}

	public bool IsCurrentConnection(string sessionId, IPluginConnection connection)
	{
		lock (_gate)
		{
			return _bySessionId.TryGetValue(sessionId, out var record) &&
				ReferenceEquals(record.Connection, connection);
		}
	}

	public Task<bool> Terminate(string sessionId, int closeCode, string reason)
		=> TerminateCore(record => record.SessionId == sessionId, closeCode, reason);

	public Task<bool> TerminateForPlugin(string pluginId, int closeCode, string reason)
		=> TerminateCore(record => record.PluginId == pluginId, closeCode, reason);

	public IReadOnlyList<PluginSessionSnapshot> Snapshot()
	{
		Prune();

		lock (_gate)
		{
			return _bySessionId.Values.Select(record =>
			{
				var lastInboundTicks = Volatile.Read(ref record.LastInboundTicks);

				return new PluginSessionSnapshot
				{
					SessionId = record.SessionId,
					PluginId = record.PluginId,
					DisplayName = record.DisplayName,
					AccessTokenId = record.AccessTokenId,
					Origin = record.Origin,
					InstanceId = record.InstanceId,
					State = record.State,
					NegotiatedVersion = record.NegotiatedVersion,
					DeclaredVersion = record.DeclaredVersion,
					DeclaredName = record.DeclaredName,
					CreatedAt = record.CreatedAt,
					ConnectedAt = record.ConnectedAt,
					DroppedAt = record.DroppedAt,
					LastInboundAt = lastInboundTicks == 0 ? null : new DateTimeOffset(lastInboundTicks, TimeSpan.Zero)
				};
			}).ToList();
		}
	}

	private async Task<bool> TerminateCore(Func<PluginSessionRecord, bool> match, int closeCode, string reason)
	{
		PluginSessionRecord? removed = null;

		lock (_gate)
		{
			foreach (var candidate in _bySessionId.Values)
			{
				if (match(candidate))
				{
					removed = candidate;
					break;
				}
			}

			if (removed is null)
			{
				return false;
			}

			_bySessionId.Remove(removed.SessionId);
			_recordsBySessionId.TryRemove(removed.SessionId, out _);
			if (_sessionIdByPluginId.TryGetValue(removed.PluginId, out var mapped) && mapped == removed.SessionId)
			{
				_sessionIdByPluginId.Remove(removed.PluginId);
			}
		}

		SessionEnded?.Invoke(this,
			new PluginSessionEndedEventArgs
			{
				PluginId = removed.PluginId, SessionId = removed.SessionId, Reason = PluginSessionEndReason.Pruned
			});

		if (removed.Connection is { } connection)
		{
			await connection.Close(closeCode, reason);
		}

		return true;
	}

	private void Prune()
	{
		var now = _timeProvider.GetUtcNow();
		List<(string PluginId, string SessionId)>? pruned = null;

		lock (_gate)
		{
			var stale = _bySessionId.Values
				.Where(record => IsStale(record, now))
				.Select(record => record.SessionId)
				.ToList();

			foreach (var sessionId in stale)
			{
				if (!_bySessionId.TryGetValue(sessionId, out var record))
				{
					continue;
				}

				_bySessionId.Remove(sessionId);
				_recordsBySessionId.TryRemove(sessionId, out _);
				if (_sessionIdByPluginId.TryGetValue(record.PluginId, out var mapped) && mapped == sessionId)
				{
					_sessionIdByPluginId.Remove(record.PluginId);
				}

				if (record.ConnectedAt is not null)
				{
					(pruned ??= []).Add((record.PluginId, sessionId));
				}
			}
		}

		if (pruned is not null)
		{
			foreach (var (pluginId, sessionId) in pruned)
			{
				SessionEnded?.Invoke(this,
					new PluginSessionEndedEventArgs
					{
						PluginId = pluginId, SessionId = sessionId, Reason = PluginSessionEndReason.Pruned
					});
			}
		}
	}

	private static bool IsStale(PluginSessionRecord record, DateTimeOffset now) => record.State switch
	{
		PluginSessionState.Dropped =>
			record.DroppedAt is { } droppedAt && now - droppedAt > ProtocolTimeouts.SessionResumeWindow,
		PluginSessionState.Awaiting => now - record.CreatedAt > _awaitingSessionLifetime,
		_ => false
	};
}
