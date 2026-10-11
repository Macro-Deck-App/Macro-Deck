using MacroDeck.Sdk.Batteries;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>
/// In-memory <see cref="IBatteryProviderContext" />, standing in for Macro Deck's battery catalog. It keeps
/// the host's rules: publishing a known id replaces that device, a list is applied all or nothing, and a
/// provider holds at most <see cref="BatteryLimits.MaxDevicesPerProvider" /> devices.
/// </summary>
public sealed class FakeBatteryProviderContext : IBatteryProviderContext
{
	private readonly Lock _sync = new();
	private readonly Dictionary<string, BatteryDevice> _devices = new(StringComparer.Ordinal);
	private readonly List<IReadOnlyList<BatteryDevice>> _publishes = [];
	private readonly List<string> _removedIds = [];

	/// <summary>The devices currently published, keyed by their provider-local id.</summary>
	public IReadOnlyDictionary<string, BatteryDevice> Devices
	{
		get
		{
			lock (_sync)
			{
				return new Dictionary<string, BatteryDevice>(_devices, StringComparer.Ordinal);
			}
		}
	}

	/// <summary>Every accepted publish call, in order, each with the devices it carried.</summary>
	public IReadOnlyList<IReadOnlyList<BatteryDevice>> Publishes
	{
		get
		{
			lock (_sync)
			{
				return [.. _publishes];
			}
		}
	}

	/// <summary>The ids removed for good, in order.</summary>
	public IReadOnlyList<string> RemovedIds
	{
		get
		{
			lock (_sync)
			{
				return [.. _removedIds];
			}
		}
	}

	/// <inheritdoc />
	public Task PublishAsync(BatteryDevice device, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(device);
		return PublishAsync([device], cancellationToken);
	}

	/// <inheritdoc />
	public Task PublishAsync(IReadOnlyList<BatteryDevice> devices, CancellationToken cancellationToken = default)
	{
		var checkedDevices = Checked(devices);
		if (checkedDevices.Count == 0)
		{
			return Task.CompletedTask;
		}

		lock (_sync)
		{
			var total = _devices.Count + checkedDevices.Count(device => !_devices.ContainsKey(device.Id));
			ThrowIfOverLimit(total);

			foreach (var device in checkedDevices)
			{
				_devices[device.Id] = device;
			}

			_publishes.Add(checkedDevices);
		}

		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task RemoveAsync(string deviceId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

		lock (_sync)
		{
			if (_devices.Remove(deviceId))
			{
				_removedIds.Add(deviceId);
			}
		}

		return Task.CompletedTask;
	}

	internal void Withdraw()
	{
		lock (_sync)
		{
			_devices.Clear();
		}
	}

	internal void Replace(IReadOnlyList<BatteryDevice> devices)
	{
		var checkedDevices = Checked(devices);

		lock (_sync)
		{
			ThrowIfOverLimit(checkedDevices.Count);

			var kept = checkedDevices.Select(device => device.Id).ToHashSet(StringComparer.Ordinal);
			_removedIds.AddRange(_devices.Keys.Where(id => !kept.Contains(id)));
			_devices.Clear();
			foreach (var device in checkedDevices)
			{
				_devices[device.Id] = device;
			}
		}
	}

	private static List<BatteryDevice> Checked(IReadOnlyList<BatteryDevice> devices)
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

		return [.. devices];
	}

	private static void ThrowIfOverLimit(int total)
	{
		if (total > BatteryLimits.MaxDevicesPerProvider)
		{
			throw new InvalidOperationException(
				$"A battery provider can publish at most {BatteryLimits.MaxDevicesPerProvider} devices.");
		}
	}
}
