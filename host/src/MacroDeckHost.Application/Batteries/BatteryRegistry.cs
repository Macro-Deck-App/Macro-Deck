using MacroDeck.Sdk.Batteries;
using MacroDeckHost.Application.Integrations;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Batteries;

public sealed class BatteryRegistry : IBatteryRegistry, IDisposable
{
	private readonly Lock _gate = new();
	private readonly Dictionary<string, Dictionary<string, Entry>> _owners = new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _providerNames = new(StringComparer.Ordinal);
	private readonly List<string> _pendingRemoved = [];
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;
	private readonly IIntegrationRegistry? _integrations;
	private bool _dirty;
	private bool _dispatching;

	public BatteryRegistry(TimeProvider timeProvider, ILogger logger, IIntegrationRegistry? integrations = null)
	{
		_timeProvider = timeProvider;
		_logger = logger.ForContext<BatteryRegistry>();
		_integrations = integrations;

		if (_integrations is not null)
		{
			_integrations.AvailabilityChanged += OnAvailabilityChanged;
		}
	}

	public event EventHandler<BatteryDevicesChangedEventArgs>? Changed;

	public IReadOnlyList<BatteryDeviceSnapshot> Devices
	{
		get
		{
			lock (_gate)
			{
				var devices = new List<BatteryDeviceSnapshot>();
				foreach (var (ownerId, entries) in _owners)
				{
					if (IsHidden(ownerId))
					{
						continue;
					}

					var providerName = _providerNames.GetValueOrDefault(ownerId) ?? string.Empty;
					devices.AddRange(
						entries.Values.Select(entry => entry.Snapshot with { ProviderName = providerName }));
				}

				devices.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
				return devices;
			}
		}
	}

	public BatteryDeviceSnapshot? Find(string id)
	{
		ArgumentNullException.ThrowIfNull(id);

		var separator = id.IndexOf(':');
		if (separator <= 0)
		{
			return null;
		}

		var ownerId = id[..separator];
		var localId = id[(separator + 1)..];

		lock (_gate)
		{
			if (IsHidden(ownerId)
				|| !_owners.TryGetValue(ownerId, out var entries)
				|| !entries.TryGetValue(localId, out var entry))
			{
				return null;
			}

			return entry.Snapshot with { ProviderName = _providerNames.GetValueOrDefault(ownerId) ?? string.Empty };
		}
	}

	public void Publish(string ownerId, string? sessionId, IReadOnlyList<BatteryDevice> devices)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
		RequireDistinct(devices);

		if (devices.Count == 0)
		{
			return;
		}

		lock (_gate)
		{
			var entries = _owners.GetValueOrDefault(ownerId);
			var total = (entries?.Count ?? 0) + devices.Count(device => entries?.ContainsKey(device.Id) != true);
			if (total > BatteryLimits.MaxDevicesPerProvider)
			{
				throw new InvalidOperationException(
					$"A battery provider can publish at most {BatteryLimits.MaxDevicesPerProvider} devices.");
			}

			if (entries is null)
			{
				entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
				_owners[ownerId] = entries;
			}

			var now = _timeProvider.GetUtcNow();
			foreach (var device in devices)
			{
				entries[device.Id] = new Entry(ToSnapshot(ownerId, device, now), sessionId);
				_pendingRemoved.Remove(Qualify(ownerId, device.Id));
			}

			if (!IsHidden(ownerId))
			{
				MarkDirty();
			}
		}
	}

	public void Remove(string ownerId, IReadOnlyList<string> localIds)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
		ArgumentNullException.ThrowIfNull(localIds);
		foreach (var localId in localIds)
		{
			if (string.IsNullOrWhiteSpace(localId))
			{
				throw new ArgumentException("A battery device id cannot be empty.", nameof(localIds));
			}
		}

		lock (_gate)
		{
			if (!_owners.TryGetValue(ownerId, out var entries))
			{
				return;
			}

			var removedAny = false;
			foreach (var localId in localIds)
			{
				if (entries.Remove(localId))
				{
					_pendingRemoved.Add(Qualify(ownerId, localId));
					removedAny = true;
				}
			}

			if (entries.Count == 0)
			{
				_owners.Remove(ownerId);
			}

			if (removedAny)
			{
				MarkDirty();
			}
		}
	}

	public void Sync(string ownerId, string? sessionId, IReadOnlyList<BatteryDevice> devices)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
		RequireDistinct(devices);

		if (devices.Count > BatteryLimits.MaxDevicesPerProvider)
		{
			throw new InvalidOperationException(
				$"A battery provider can publish at most {BatteryLimits.MaxDevicesPerProvider} devices.");
		}

		lock (_gate)
		{
			var now = _timeProvider.GetUtcNow();
			var next = new Dictionary<string, Entry>(StringComparer.Ordinal);
			foreach (var device in devices)
			{
				next[device.Id] = new Entry(ToSnapshot(ownerId, device, now), sessionId);
				_pendingRemoved.Remove(Qualify(ownerId, device.Id));
			}

			var removedAny = false;
			if (_owners.TryGetValue(ownerId, out var previous))
			{
				foreach (var localId in previous.Keys.Where(localId => !next.ContainsKey(localId)))
				{
					_pendingRemoved.Add(Qualify(ownerId, localId));
					removedAny = true;
				}
			}

			if (next.Count == 0)
			{
				_owners.Remove(ownerId);
			}
			else
			{
				_owners[ownerId] = next;
			}

			if (removedAny || !IsHidden(ownerId))
			{
				MarkDirty();
			}
		}
	}

	public void RemoveAll(string ownerId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

		lock (_gate)
		{
			if (_owners.Remove(ownerId) && !IsHidden(ownerId))
			{
				MarkDirty();
			}
		}
	}

	public void RemoveSession(string ownerId, string sessionId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
		ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

		lock (_gate)
		{
			if (!_owners.TryGetValue(ownerId, out var entries))
			{
				return;
			}

			var ended = entries
				.Where(pair => string.Equals(pair.Value.SessionId, sessionId, StringComparison.Ordinal))
				.Select(pair => pair.Key)
				.ToList();
			if (ended.Count == 0)
			{
				return;
			}

			foreach (var localId in ended)
			{
				entries.Remove(localId);
			}

			if (entries.Count == 0)
			{
				_owners.Remove(ownerId);
			}

			if (!IsHidden(ownerId))
			{
				MarkDirty();
			}
		}
	}

	public void SetProviderName(string ownerId, string providerName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
		providerName ??= string.Empty;

		lock (_gate)
		{
			if (string.Equals(_providerNames.GetValueOrDefault(ownerId), providerName, StringComparison.Ordinal))
			{
				return;
			}

			_providerNames[ownerId] = providerName;
			if (_owners.ContainsKey(ownerId) && !IsHidden(ownerId))
			{
				MarkDirty();
			}
		}
	}

	public void Dispose()
	{
		if (_integrations is not null)
		{
			_integrations.AvailabilityChanged -= OnAvailabilityChanged;
		}
	}

	private void OnAvailabilityChanged(object? sender, IntegrationAvailabilityChangedEventArgs e)
	{
		lock (_gate)
		{
			if (_owners.ContainsKey(e.IntegrationId))
			{
				MarkDirty();
			}
		}
	}

	private bool IsHidden(string ownerId)
	{
		return _integrations?.IsExplicitlyDisabled(ownerId) == true;
	}

	private void MarkDirty()
	{
		_dirty = true;
		if (_dispatching)
		{
			return;
		}

		_dispatching = true;
		_ = Task.Run(Dispatch);
	}

	// One dispatcher at a time, off the publishing thread: changes made while handlers run are coalesced
	// into the next raise, so readers see them in order and a slow handler never blocks a provider.
	private void Dispatch()
	{
		while (true)
		{
			string[] removedIds;
			lock (_gate)
			{
				if (!_dirty)
				{
					_dispatching = false;
					return;
				}

				_dirty = false;
				removedIds = [.. _pendingRemoved.Distinct(StringComparer.Ordinal)];
				_pendingRemoved.Clear();
			}

			Raise(new BatteryDevicesChangedEventArgs(removedIds));
		}
	}

	private void Raise(BatteryDevicesChangedEventArgs args)
	{
		if (Changed is not { } changed)
		{
			return;
		}

		foreach (var handler in changed.GetInvocationList().Cast<EventHandler<BatteryDevicesChangedEventArgs>>())
		{
			try
			{
				handler(this, args);
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				_logger.Warning(exception, "A battery device change handler failed");
			}
		}
	}

	private static void RequireDistinct(IReadOnlyList<BatteryDevice> devices)
	{
		ArgumentNullException.ThrowIfNull(devices);

		var ids = new HashSet<string>(StringComparer.Ordinal);
		foreach (var device in devices)
		{
			if (device is null)
			{
				throw new ArgumentException("A battery device list cannot contain null.", nameof(devices));
			}

			if (!ids.Add(device.Id))
			{
				throw new ArgumentException($"The battery device '{device.Id}' is listed twice.", nameof(devices));
			}
		}
	}

	private static string Qualify(string ownerId, string localId)
	{
		return $"{ownerId}:{localId}";
	}

	private static BatteryDeviceSnapshot ToSnapshot(string ownerId, BatteryDevice device, DateTimeOffset now)
	{
		return new BatteryDeviceSnapshot
		{
			Id = Qualify(ownerId, device.Id),
			LocalId = device.Id,
			ProviderId = ownerId,
			Name = device.Name,
			Kind = BatteryDeviceKinds.Normalize(device.Kind),
			LevelPercent = device.LevelPercent,
			ChargeState = device.ChargeState,
			IsConnected = device.IsConnected,
			TimeRemaining = device.TimeRemaining,
			TimeToFull = device.TimeToFull,
			Health = device.Health,
			UpdatedAt = now
		};
	}

	private sealed record Entry(BatteryDeviceSnapshot Snapshot, string? SessionId);
}
