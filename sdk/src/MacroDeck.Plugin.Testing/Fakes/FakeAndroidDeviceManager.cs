using MacroDeck.Sdk.Android;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>
/// In-memory <see cref="IAndroidDeviceManager" /> for testing a plugin's ADB logic without adb or a device.
/// Register it with <c>AddSingleton</c> in <c>PluginHostBuilder.ConfigureServices</c>, which replaces the SDK's
/// own registration, or use it directly in a unit test. Access starts as
/// <see cref="AndroidDeviceAccess.Available" /> with no devices; the test host itself offers no ADB, so a plugin
/// that does not use this fake sees
/// <see cref="AndroidDeviceAccess.Unsupported" />.
/// </summary>
public sealed class FakeAndroidDeviceManager : IAndroidDeviceManager
{
	private readonly Lock _gate = new();
	private readonly List<FakeAndroidDevice> _devices = [];
	private readonly List<string> _connectedAddresses = [];
	private AndroidDeviceAccess _access = AndroidDeviceAccess.Available;

	/// <inheritdoc />
	public event EventHandler<AndroidDeviceEventArgs>? DeviceConnected;

	/// <inheritdoc />
	public event EventHandler<AndroidDeviceEventArgs>? DeviceDisconnected;

	/// <inheritdoc />
	public event EventHandler<AndroidDeviceEventArgs>? DeviceStateChanged;

	/// <inheritdoc />
	public event EventHandler? AccessChanged;

	/// <inheritdoc />
	public AndroidDeviceAccess Access
	{
		get
		{
			lock (_gate)
			{
				return _access;
			}
		}
	}

	/// <inheritdoc />
	public IReadOnlyCollection<IAndroidDevice> Devices
	{
		get
		{
			lock (_gate)
			{
				return _access == AndroidDeviceAccess.Available ? [.. _devices] : [];
			}
		}
	}

	/// <inheritdoc />
	public IAndroidDevice? FindDevice(string serial)
		=> Devices.FirstOrDefault(device => string.Equals(device.Serial, serial, StringComparison.Ordinal));

	/// <summary>Every address passed to <see cref="ConnectAsync" />, in call order, including refused ones.</summary>
	public IReadOnlyList<string> ConnectedAddresses
	{
		get
		{
			lock (_gate)
			{
				return [.. _connectedAddresses];
			}
		}
	}

	/// <inheritdoc />
	/// <remarks>Attaches an <see cref="AndroidDeviceState.Online" /> device named after the address, or returns the
	/// one already attached. Refused like a device operation while <see cref="Access" /> is not
	/// <see cref="AndroidDeviceAccess.Available" />.</remarks>
	public Task<IAndroidDevice> ConnectAsync(string address, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(address);
		lock (_gate)
		{
			_connectedAddresses.Add(address);
		}

		cancellationToken.ThrowIfCancellationRequested();
		var refusal = Access switch
		{
			AndroidDeviceAccess.Available => (AndroidDeviceErrorCode?)null,
			AndroidDeviceAccess.AdbNotEnabled => AndroidDeviceErrorCode.AdbNotEnabled,
			AndroidDeviceAccess.AdbNotAllowed => AndroidDeviceErrorCode.AdbNotAllowed,
			_ => AndroidDeviceErrorCode.Unsupported
		};
		if (refusal is { } code)
		{
			throw new AndroidDeviceException(code, $"The fake device manager refused connect with {code}.");
		}

		return Task.FromResult(FindDevice(address) ?? AddDevice(address));
	}

	/// <summary>Changes <see cref="Access" /> and raises <see cref="AccessChanged" />. While it is not
	/// <see cref="AndroidDeviceAccess.Available" />, <see cref="Devices" /> is empty and every device operation throws
	/// the matching <see cref="AndroidDeviceException" />, as with a real host.</summary>
	public void SetAccess(AndroidDeviceAccess access)
	{
		lock (_gate)
		{
			if (_access == access)
			{
				return;
			}

			_access = access;
		}

		AccessChanged?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>Attaches a device and raises <see cref="DeviceConnected" />.</summary>
	public FakeAndroidDevice AddDevice(string serial,
		AndroidDeviceState state = AndroidDeviceState.Online,
		AndroidDeviceInfo? info = null)
	{
		var device = new FakeAndroidDevice(this, serial, info ?? new AndroidDeviceInfo(null, null, null), state);
		lock (_gate)
		{
			if (_devices.Any(existing => string.Equals(existing.Serial, serial, StringComparison.Ordinal)))
			{
				throw new InvalidOperationException($"A device with serial '{serial}' is already attached.");
			}

			_devices.Add(device);
		}

		DeviceConnected?.Invoke(this, new AndroidDeviceEventArgs(device, null));
		return device;
	}

	/// <summary>Detaches a device and raises <see cref="DeviceDisconnected" />. False when it was not attached.</summary>
	public bool RemoveDevice(string serial)
	{
		FakeAndroidDevice? removed;
		lock (_gate)
		{
			removed = _devices.FirstOrDefault(device => string.Equals(device.Serial, serial, StringComparison.Ordinal));
			if (removed is null)
			{
				return false;
			}

			_devices.Remove(removed);
		}

		DeviceDisconnected?.Invoke(this, new AndroidDeviceEventArgs(removed, null));
		return true;
	}

	/// <summary>Changes an attached device's state and raises <see cref="DeviceStateChanged" />.</summary>
	public void SetDeviceState(string serial, AndroidDeviceState state)
	{
		FakeAndroidDevice device;
		lock (_gate)
		{
			device = _devices.FirstOrDefault(existing => string.Equals(existing.Serial, serial, StringComparison.Ordinal)) ??
				throw new InvalidOperationException($"No device with serial '{serial}' is attached.");
		}

		var previous = device.State;
		if (previous == state)
		{
			return;
		}

		device.SetState(state);
		DeviceStateChanged?.Invoke(this, new AndroidDeviceEventArgs(device, previous));
	}
}
