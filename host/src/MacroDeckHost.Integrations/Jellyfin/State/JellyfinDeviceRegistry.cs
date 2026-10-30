using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MacroDeckHost.Integrations.Jellyfin.Configuration;
using MacroDeckHost.Integrations.Jellyfin.Variables;

namespace MacroDeckHost.Integrations.Jellyfin.State;

internal sealed record JellyfinKnownDevice(
	string DeviceId,
	string Name,
	string Client,
	string Key,
	DateTimeOffset LastSeen,
	string LocalId,
	bool IsControllable = true,
	string? Address = null);

internal sealed class JellyfinDeviceRegistry
{
	public static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(30);

	private static readonly TimeSpan _lastSeenResolution = TimeSpan.FromDays(1);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, JellyfinKnownDevice> _devices = new(StringComparer.Ordinal);

	public IReadOnlyList<JellyfinKnownDevice> Devices
	{
		get
		{
			lock (_sync)
			{
				return [.. _devices.Values.OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)];
			}
		}
	}

	public static string LocalIdFor(string deviceId)
	{
		var hash = SHA256.HashData(Encoding.UTF8.GetBytes(deviceId));
		return Convert.ToHexStringLower(hash.AsSpan(0, 4));
	}

	public JellyfinKnownDevice? FindByLocalId(string localId)
	{
		lock (_sync)
		{
			return _devices.Values.FirstOrDefault(device
				=> string.Equals(device.LocalId, localId, StringComparison.Ordinal));
		}
	}

	public JellyfinKnownDevice? Find(string deviceId)
	{
		lock (_sync)
		{
			return _devices.GetValueOrDefault(deviceId);
		}
	}

	public void Load(string? json)
	{
		var loaded = Parse(json);
		lock (_sync)
		{
			_devices.Clear();
			foreach (var device in loaded)
			{
				_devices[device.DeviceId] = device;
			}
		}
	}

	public string Serialize()
	{
		lock (_sync)
		{
			return JsonSerializer.Serialize(_devices.Values.OrderBy(device => device.DeviceId, StringComparer.Ordinal)
				.Select(device => new StoredDevice(device.DeviceId,
					device.Name,
					device.Client,
					device.Key,
					device.LastSeen,
					device.IsControllable,
					device.LocalId,
					device.Address))
				.ToList());
		}
	}

	public IReadOnlySet<string> Keys
	{
		get
		{
			lock (_sync)
			{
				return _devices.Values.Select(device => device.Key).ToHashSet(StringComparer.Ordinal);
			}
		}
	}

	// Some clients, Google Cast among them, report a new DeviceId per connection, and Jellyfin keeps listing the
	// old session until logout or restart, so an idle older namesake session is a leftover, not a new device.
	public bool Observe(IReadOnlyList<JellyfinSession> sessions, DateTimeOffset now, Func<string, bool> isKeyTaken)
	{
		var changed = false;
		lock (_sync)
		{
			var observed = sessions.Where(session => session.SupportsMediaControl || session.IsActive).ToList();
			var listed = observed.GroupBy(session => session.DeviceId, StringComparer.Ordinal)
				.ToDictionary(group => group.Key,
					group => new ListedDevice(group.Any(session => session.IsActive),
						group.Max(session => session.LastActivity)),
					StringComparer.Ordinal);

			var present = new HashSet<string>(StringComparer.Ordinal);
			var takenOver = new HashSet<string>(StringComparer.Ordinal);
			foreach (var session in observed
				.OrderBy(session => _devices.ContainsKey(session.DeviceId) ? 0 : 1)
				.ThenByDescending(session => session.LastActivity))
			{
				if (_devices.TryGetValue(session.DeviceId, out var known))
				{
					present.Add(session.DeviceId);
					changed |= Update(known, session, now);
					continue;
				}

				var namesakes = _devices.Values
					.Where(device => string.Equals(device.Name, session.DeviceName, StringComparison.Ordinal)
						&& string.Equals(device.Client, session.Client, StringComparison.Ordinal)
						&& SameAddress(device.Address, session.Address))
					.Select(device => (Device: device, Current: listed.GetValueOrDefault(device.DeviceId)))
					.ToList();

				if (!session.IsActive
					&& namesakes.Any(candidate => candidate.Current is { } current
						&& current.LastActivity >= session.LastActivity))
				{
					continue;
				}

				var target = namesakes
					.Where(candidate => !takenOver.Contains(candidate.Device.LocalId)
						&& (candidate.Current is null
							|| !candidate.Current.IsPlaying && candidate.Current.LastActivity < session.LastActivity))
					.OrderByDescending(candidate => candidate.Current?.LastActivity)
					.ThenByDescending(candidate => candidate.Device.LastSeen)
					.ThenBy(candidate => candidate.Device.Key, StringComparer.Ordinal)
					.Select(candidate => candidate.Device)
					.FirstOrDefault();

				if (target is not null)
				{
					_devices.Remove(target.DeviceId);
					_devices[session.DeviceId] = target with
					{
						DeviceId = session.DeviceId,
						IsControllable = target.IsControllable || session.SupportsMediaControl,
						LastSeen = now,
						Address = session.Address ?? target.Address
					};
					takenOver.Add(target.LocalId);
					present.Add(session.DeviceId);
					changed = true;
					continue;
				}

				_devices[session.DeviceId] = new JellyfinKnownDevice(session.DeviceId,
					session.DeviceName,
					session.Client,
					AllocateKey(session.DeviceName, isKeyTaken),
					now,
					AllocateLocalId(session.DeviceId),
					session.SupportsMediaControl,
					session.Address);
				present.Add(session.DeviceId);
				changed = true;
			}

			foreach (var stale in _devices.Values
				.Where(device => !present.Contains(device.DeviceId) && now - device.LastSeen > RetentionPeriod)
				.Select(device => device.DeviceId)
				.ToList())
			{
				_devices.Remove(stale);
				changed = true;
			}
		}

		return changed;
	}

	private bool Update(JellyfinKnownDevice known, JellyfinSession session, DateTimeOffset now)
	{
		var updated = known with
		{
			Name = session.DeviceName,
			Client = session.Client,
			IsControllable = known.IsControllable || session.SupportsMediaControl,
			LastSeen = now - known.LastSeen >= _lastSeenResolution ? now : known.LastSeen,
			Address = session.Address ?? known.Address
		};

		if (updated == known)
		{
			return false;
		}

		_devices[session.DeviceId] = updated;
		return true;
	}

	private static bool SameAddress(string? known, string? observed)
	{
		return known is null || observed is null || string.Equals(known, observed, StringComparison.Ordinal);
	}

	private string AllocateLocalId(string deviceId)
	{
		var used = _devices.Values.Select(device => device.LocalId).ToHashSet(StringComparer.Ordinal);
		var candidate = LocalIdFor(deviceId);
		for (var suffix = 2; used.Contains(candidate); suffix++)
		{
			candidate = LocalIdFor($"{deviceId}#{suffix}");
		}

		return candidate;
	}

	private string AllocateKey(string deviceName, Func<string, bool> isKeyTaken)
	{
		var stem = JellyfinKeys.Stem(deviceName, "device", JellyfinVariables.MaxDeviceKeyLength - 3);

		var used = _devices.Values.Select(device => device.Key).ToHashSet(StringComparer.Ordinal);
		for (var suffix = 1;; suffix++)
		{
			var candidate = suffix == 1 ? stem : $"{stem}_{suffix}";
			if (!used.Contains(candidate) && !isKeyTaken(candidate))
			{
				return candidate;
			}
		}
	}

	private static List<JellyfinKnownDevice> Parse(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return [];
		}

		try
		{
			return (JsonSerializer.Deserialize<List<StoredDevice>>(json) ?? [])
				.Where(device => device is { DeviceId.Length: > 0, Key.Length: > 0 })
				.Select(device => new JellyfinKnownDevice(device.DeviceId,
					string.IsNullOrEmpty(device.Name) ? device.DeviceId : device.Name,
					device.Client ?? string.Empty,
					device.Key,
					device.LastSeen,
					string.IsNullOrEmpty(device.LocalId) ? LocalIdFor(device.DeviceId) : device.LocalId,
					device.IsControllable ?? true,
					device.Address))
				.ToList();
		}
		catch (JsonException)
		{
			return [];
		}
	}

	private sealed record StoredDevice(
		string DeviceId,
		string Name,
		string? Client,
		string Key,
		DateTimeOffset LastSeen,
		bool? IsControllable,
		string? LocalId = null,
		string? Address = null);

	private sealed record ListedDevice(bool IsPlaying, DateTimeOffset LastActivity);
}
