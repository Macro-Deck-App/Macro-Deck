using MacroDeck.Sdk.Android;

namespace MacroDeckHost.Integrations.Adb.Connections;

internal enum AdbDeviceStatus
{
	Device,
	Authorizing,
	Unauthorized,
	NoPermissions,
	Offline,
	Bootloader,
	Recovery,
	Sideload,
	Unknown,
	Disconnected
}

internal enum AdbDeviceChangeKind
{
	Connected,
	Disconnected,
	Authorized,
	Unauthorized,
	Online,
	Offline
}

internal sealed record AdbTrackedDevice(
	IAndroidDevice Device,
	AdbDeviceStatus Status,
	string? Model,
	string? Manufacturer)
{
	public string Serial => Device.Serial;

	public bool IsAuthorized => Status == AdbDeviceStatus.Device;
}

internal sealed record AdbDeviceChange(AdbDeviceChangeKind Kind, AdbTrackedDevice Device);

// A device that went away is kept for a while so that it comes back as Online rather than Connected.
internal sealed class AdbDeviceTracker : IDisposable
{
	internal static readonly TimeSpan DisconnectedRetention = TimeSpan.FromMinutes(5);
	internal const int MaxDisconnectedDevices = 32;

	private readonly IAndroidDeviceManager _manager;
	private readonly TimeProvider _timeProvider;
	private readonly Lock _gate = new();
	private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

	public AdbDeviceTracker(IAndroidDeviceManager manager, TimeProvider timeProvider)
	{
		_manager = manager;
		_timeProvider = timeProvider;
		lock (_gate)
		{
			foreach (var device in manager.Devices)
			{
				_entries[device.Serial] = new Entry(Track(device, StatusOf(device)), null);
			}
		}

		manager.DeviceConnected += OnDeviceChanged;
		manager.DeviceStateChanged += OnDeviceChanged;
		manager.DeviceDisconnected += OnDeviceDisconnected;
	}

	public event EventHandler<AdbDeviceChange>? Changed;

	public IReadOnlyList<AdbTrackedDevice> Devices
	{
		get
		{
			lock (_gate)
			{
				Prune();
				return [.. _entries.Values.Select(entry => entry.Device)];
			}
		}
	}

	public AdbTrackedDevice? Find(string serial)
	{
		lock (_gate)
		{
			Prune();
			return _entries.GetValueOrDefault(serial)?.Device;
		}
	}

	public void Dispose()
	{
		_manager.DeviceConnected -= OnDeviceChanged;
		_manager.DeviceStateChanged -= OnDeviceChanged;
		_manager.DeviceDisconnected -= OnDeviceDisconnected;
	}

	internal static AdbDeviceStatus StatusOf(IAndroidDevice device)
	{
		return device.State switch
		{
			AndroidDeviceState.Online => AdbDeviceStatus.Device,
			AndroidDeviceState.Connecting => AdbDeviceStatus.Authorizing,
			AndroidDeviceState.Unauthorized when device.LacksUsbPermission => AdbDeviceStatus.NoPermissions,
			AndroidDeviceState.Unauthorized => AdbDeviceStatus.Unauthorized,
			_ => device.Mode switch
			{
				AndroidDeviceMode.Bootloader => AdbDeviceStatus.Bootloader,
				AndroidDeviceMode.Recovery => AdbDeviceStatus.Recovery,
				AndroidDeviceMode.Sideload => AdbDeviceStatus.Sideload,
				AndroidDeviceMode.Unknown => AdbDeviceStatus.Unknown,
				_ => AdbDeviceStatus.Offline
			}
		};
	}

	internal static AdbDeviceChangeKind? KindFor(AdbDeviceStatus? previous, AdbDeviceStatus current)
	{
		if (previous is not { } before)
		{
			return AdbDeviceChangeKind.Connected;
		}

		if (before == current)
		{
			return null;
		}

		if (before == AdbDeviceStatus.Disconnected)
		{
			return AdbDeviceChangeKind.Online;
		}

		return current switch
		{
			AdbDeviceStatus.Disconnected => AdbDeviceChangeKind.Disconnected,
			AdbDeviceStatus.Device => AdbDeviceChangeKind.Authorized,
			AdbDeviceStatus.Unauthorized => AdbDeviceChangeKind.Unauthorized,
			AdbDeviceStatus.Offline => AdbDeviceChangeKind.Offline,
			_ => null
		};
	}

	private void OnDeviceChanged(object? sender, AndroidDeviceEventArgs e)
	{
		Apply(e.Device, StatusOf(e.Device));
	}

	private void OnDeviceDisconnected(object? sender, AndroidDeviceEventArgs e)
	{
		Apply(e.Device, AdbDeviceStatus.Disconnected);
	}

	private void Apply(IAndroidDevice device, AdbDeviceStatus status)
	{
		AdbDeviceChange? change;
		lock (_gate)
		{
			Prune();
			var previous = _entries.GetValueOrDefault(device.Serial);
			if (previous is null && status == AdbDeviceStatus.Disconnected)
			{
				return;
			}

			var tracked = status == AdbDeviceStatus.Disconnected
				? previous!.Device with { Device = device, Status = status }
				: Track(device, status);
			_entries[device.Serial] = new Entry(tracked,
				status == AdbDeviceStatus.Disconnected ? _timeProvider.GetUtcNow() : null);
			if (status == AdbDeviceStatus.Disconnected)
			{
				TrimDisconnected();
			}

			change = KindFor(previous?.Device.Status, status) is { } kind ? new AdbDeviceChange(kind, tracked) : null;
		}

		if (change is not null)
		{
			Changed?.Invoke(this, change);
		}
	}

	private static AdbTrackedDevice Track(IAndroidDevice device, AdbDeviceStatus status)
	{
		return new AdbTrackedDevice(device, status, device.Info.Model, device.Info.Manufacturer);
	}

	private void Prune()
	{
		var now = _timeProvider.GetUtcNow();
		foreach (var (serial, entry) in _entries.ToList())
		{
			if (entry.DisconnectedAt is { } at && now - at > DisconnectedRetention)
			{
				_entries.Remove(serial);
			}
		}
	}

	private void TrimDisconnected()
	{
		foreach (var serial in _entries
			.Where(pair => pair.Value.DisconnectedAt is not null)
			.OrderByDescending(pair => pair.Value.DisconnectedAt)
			.Skip(MaxDisconnectedDevices)
			.Select(pair => pair.Key)
			.ToList())
		{
			_entries.Remove(serial);
		}
	}

	private sealed record Entry(AdbTrackedDevice Device, DateTimeOffset? DisconnectedAt);
}
