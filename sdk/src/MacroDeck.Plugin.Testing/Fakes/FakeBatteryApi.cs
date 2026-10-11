using MacroDeck.Sdk.Batteries;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>
/// In-memory <see cref="IBatteryApi" />. Seed the devices a plugin under test reads with <see cref="Set" />;
/// every call raises <see cref="Changed" /> before it returns.
/// </summary>
public sealed class FakeBatteryApi : IBatteryApi
{
	private readonly Lock _sync = new();
	private IReadOnlyList<BatteryDeviceSnapshot> _devices = [];

	/// <inheritdoc />
	public IReadOnlyList<BatteryDeviceSnapshot> Devices
	{
		get
		{
			lock (_sync)
			{
				return _devices;
			}
		}
	}

	/// <inheritdoc />
	public event EventHandler<BatteryDevicesChangedEventArgs>? Changed;

	/// <summary>The number of handlers currently subscribed to <see cref="Changed" />.</summary>
	public int SubscriberCount => Changed?.GetInvocationList().Length ?? 0;

	/// <summary>
	/// Replaces the devices and raises <see cref="Changed" />. <paramref name="removedIds" /> are the qualified
	/// ids reported as removed for good; a device that simply disappears counts as withdrawn.
	/// </summary>
	public void Set(IEnumerable<BatteryDeviceSnapshot> devices, IReadOnlyList<string>? removedIds = null)
	{
		ArgumentNullException.ThrowIfNull(devices);

		lock (_sync)
		{
			_devices = [.. devices];
		}

		Changed?.Invoke(this, new BatteryDevicesChangedEventArgs(removedIds ?? []));
	}

	/// <inheritdoc />
	public BatteryDeviceSnapshot? Find(string id)
	{
		ArgumentNullException.ThrowIfNull(id);
		return Devices.FirstOrDefault(device => string.Equals(device.Id, id, StringComparison.Ordinal));
	}
}
