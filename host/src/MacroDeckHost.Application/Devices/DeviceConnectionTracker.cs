using System.Collections.Concurrent;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Triggers.Providers;

namespace MacroDeckHost.Application.Devices;

public sealed class DeviceConnectionTracker
{
	private sealed class ConnectionEntry
	{
		public Guid? DeviceId;
		public string? ClientId;
		public string? Key;
		public Action? Abort;
	}

	private readonly record struct PendingDisconnect(DateTime At, Guid? DeviceId, string? ClientId);

	private readonly ConcurrentDictionary<string, ConnectionEntry> _connections = new(StringComparer.Ordinal);
	private readonly Dictionary<string, int> _countsByKey = new(StringComparer.Ordinal);
	private readonly Dictionary<string, PendingDisconnect> _pendingDisconnects = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<Guid, DateTime> _revokedUntil = new();
	private readonly Lock _sync = new();

	private readonly IEventBus _bus;
	private readonly TimeProvider _timeProvider;

	private volatile IReadOnlyDictionary<Guid, string> _deviceNames = new Dictionary<Guid, string>();

	public DeviceConnectionTracker(IEventBus bus, TimeProvider timeProvider)
	{
		_bus = bus;
		_timeProvider = timeProvider;
	}

	public void Attach(string connectionId, Guid? deviceId, Action abort)
	{
		var entry = GetOrAdd(connectionId);
		lock (_sync)
		{
			entry.DeviceId = deviceId;
			entry.Abort = abort;
		}
	}

	public void Register(string connectionId, string clientId)
	{
		var entry = GetOrAdd(connectionId);

		bool isFirst;
		bool suppressed;
		lock (_sync)
		{
			if (entry.Key is not null)
			{
				return;
			}

			if (entry.DeviceId is null && string.IsNullOrWhiteSpace(clientId))
			{
				return;
			}

			entry.ClientId = clientId;
			var key = KeyFor(entry.DeviceId, clientId);
			entry.Key = key;

			var count = _countsByKey.GetValueOrDefault(key);
			_countsByKey[key] = count + 1;
			isFirst = count == 0;
			suppressed = isFirst && _pendingDisconnects.Remove(key);
		}

		if (isFirst && !suppressed)
		{
			Publish(EventIds.ClientConnected, entry.DeviceId, clientId);
		}
	}

	public void Remove(string connectionId)
	{
		if (!_connections.TryRemove(connectionId, out var entry))
		{
			return;
		}

		lock (_sync)
		{
			if (entry.Key is null)
			{
				return;
			}

			var count = _countsByKey.GetValueOrDefault(entry.Key) - 1;
			if (count <= 0)
			{
				_countsByKey.Remove(entry.Key);
				_pendingDisconnects[entry.Key]
					= new PendingDisconnect(_timeProvider.GetUtcNow().UtcDateTime, entry.DeviceId, entry.ClientId);
			}
			else
			{
				_countsByKey[entry.Key] = count;
			}
		}
	}

	public void FlushPendingDisconnects(DateTime now)
	{
		PruneExpiredRevocations(now);

		var window = TimeSpan.FromSeconds(DeviceDefaults.PresenceLingerSeconds);
		List<(Guid? DeviceId, string? ClientId)>? due = null;

		lock (_sync)
		{
			foreach (var (key, pending) in _pendingDisconnects)
			{
				if (now - pending.At < window)
				{
					continue;
				}

				(due ??= []).Add((pending.DeviceId, pending.ClientId));
				_pendingDisconnects.Remove(key);
			}
		}

		if (due is null)
		{
			return;
		}

		foreach (var (deviceId, clientId) in due)
		{
			Publish(EventIds.ClientDisconnected, deviceId, clientId);
		}
	}

	public IReadOnlyDictionary<Guid, int> OnlineDeviceConnectionCounts()
	{
		var now = _timeProvider.GetUtcNow().UtcDateTime;
		var window = TimeSpan.FromSeconds(DeviceDefaults.PresenceLingerSeconds);
		var counts = new Dictionary<Guid, int>();

		lock (_sync)
		{
			foreach (var entry in _connections.Values)
			{
				if (entry.Key is null || entry.DeviceId is not { } id)
				{
					continue;
				}

				counts[id] = counts.GetValueOrDefault(id) + 1;
			}

			foreach (var pending in _pendingDisconnects.Values)
			{
				if (pending.DeviceId is not { } id || now - pending.At >= window)
				{
					continue;
				}

				counts.TryAdd(id, 1);
			}
		}

		return counts;
	}

	public void AbortDevice(Guid deviceId)
	{
		Action?[] aborts;
		lock (_sync)
		{
			aborts = _connections.Values.Where(entry => entry.DeviceId == deviceId).Select(entry => entry.Abort)
				.ToArray();
		}

		foreach (var abort in aborts)
		{
			abort?.Invoke();
		}
	}

	public void AbortAll()
	{
		Action?[] aborts;
		lock (_sync)
		{
			aborts = _connections.Values.Select(entry => entry.Abort).ToArray();
		}

		foreach (var abort in aborts)
		{
			abort?.Invoke();
		}
	}

	public void RevokeUntil(Guid deviceId, DateTime until) => _revokedUntil[deviceId] = until;

	public bool IsRevoked(Guid deviceId)
		=> _revokedUntil.TryGetValue(deviceId, out var until) && _timeProvider.GetUtcNow().UtcDateTime < until;

	private void PruneExpiredRevocations(DateTime now)
	{
		foreach (var entry in _revokedUntil)
		{
			if (now >= entry.Value)
			{
				// Compare-and-remove: a concurrent RevokeUntil extending this device's window between
				// the enumeration and here must not be clobbered by removing its fresh value.
				_revokedUntil.TryRemove(entry);
			}
		}
	}

	public void SetDeviceNames(IReadOnlyDictionary<Guid, string> names)
	{
		lock (_sync)
		{
			_deviceNames = names;
		}
	}

	public void SetDeviceName(Guid deviceId, string name)
	{
		lock (_sync)
		{
			_deviceNames = new Dictionary<Guid, string>(_deviceNames) { [deviceId] = name };
		}
	}

	private ConnectionEntry GetOrAdd(string connectionId)
		=> _connections.GetOrAdd(connectionId, static _ => new ConnectionEntry());

	private static string KeyFor(Guid? deviceId, string? clientId)
		=> deviceId is { } id ? $"device:{id:D}" : $"client:{clientId}";

	private void Publish(string eventId, Guid? deviceId, string? clientId)
	{
		var deviceName = deviceId is { } id && _deviceNames.TryGetValue(id, out var name) ? name : null;
		_bus.Publish(new EventOccurrence(EventIds.Qualify(eventId),
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["clientId"] = clientId ?? string.Empty,
				["deviceId"] = deviceId?.ToString(),
				["deviceName"] = deviceName
			}));
	}
}
