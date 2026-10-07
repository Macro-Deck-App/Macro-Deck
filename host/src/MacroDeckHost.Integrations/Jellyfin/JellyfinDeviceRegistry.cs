using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MacroDeckHost.Integrations.Jellyfin;

internal sealed record JellyfinKnownDevice(
	string DeviceId,
	string Name,
	string Client,
	string Key,
	DateTimeOffset LastSeen,
	bool IsControllable = true)
{
	public string LocalId => JellyfinDeviceRegistry.LocalIdFor(DeviceId);
}

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
			return _devices.Values.FirstOrDefault(device => string.Equals(device.LocalId, localId, StringComparison.Ordinal));
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
					device.IsControllable))
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

	public bool Observe(IReadOnlyList<JellyfinSession> sessions, DateTimeOffset now, Func<string, bool> isKeyTaken)
	{
		var changed = false;
		lock (_sync)
		{
			var present = new HashSet<string>(StringComparer.Ordinal);
			foreach (var session in sessions.Where(session => session.SupportsMediaControl || session.IsActive))
			{
				present.Add(session.DeviceId);
				if (_devices.TryGetValue(session.DeviceId, out var known))
				{
					var updated = known with
					{
						Name = session.DeviceName,
						Client = session.Client,
						IsControllable = known.IsControllable || session.SupportsMediaControl,
						LastSeen = now - known.LastSeen >= _lastSeenResolution ? now : known.LastSeen
					};

					if (updated != known)
					{
						_devices[session.DeviceId] = updated;
						changed = true;
					}

					continue;
				}

				var key = AllocateKey(session.DeviceName, isKeyTaken);
				_devices[session.DeviceId] = new JellyfinKnownDevice(session.DeviceId,
					session.DeviceName,
					session.Client,
					key,
					now,
					session.SupportsMediaControl);
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
					device.IsControllable ?? true))
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
		bool? IsControllable);
}
