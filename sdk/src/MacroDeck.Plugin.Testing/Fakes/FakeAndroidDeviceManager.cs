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
	private readonly Dictionary<string, FakeAndroidDevice> _known = new(StringComparer.Ordinal);
	private AndroidDeviceAccess _access = AndroidDeviceAccess.Available;
	private string? _defaultDeviceSerial;
	private bool _adbUnavailable;

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
	{
		return Devices.FirstOrDefault(device => string.Equals(device.Serial, serial, StringComparison.Ordinal));
	}

	/// <inheritdoc />
	/// <remarks>The device named by <see cref="DefaultDeviceSerial" />, attached or added and removed earlier, or the only
	/// attached device when it is <c>null</c>.</remarks>
	public IAndroidDevice? DefaultDevice
	{
		get
		{
			lock (_gate)
			{
				if (_access != AndroidDeviceAccess.Available)
				{
					return null;
				}

				if (_defaultDeviceSerial is { } serial)
				{
					return _known.GetValueOrDefault(serial);
				}

				return _devices.Count == 1 ? _devices[0] : null;
			}
		}
	}

	/// <inheritdoc />
	/// <remarks>Changed with <see cref="SetAdbUnavailable" />. Operations are not refused by it: set
	/// <see cref="FakeAndroidDevice.Failure" /> as well when the test needs that.</remarks>
	public bool IsAdbUnavailable
	{
		get
		{
			lock (_gate)
			{
				return _adbUnavailable;
			}
		}
	}

	/// <summary>Changes <see cref="IsAdbUnavailable" /> and raises <see cref="AccessChanged" />.</summary>
	public void SetAdbUnavailable(bool adbUnavailable)
	{
		lock (_gate)
		{
			if (_adbUnavailable == adbUnavailable)
			{
				return;
			}

			_adbUnavailable = adbUnavailable;
		}

		AccessChanged?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>The serial of the default device the user picked in the ADB settings, or <c>null</c> for none.</summary>
	public string? DefaultDeviceSerial
	{
		get
		{
			lock (_gate)
			{
				return _defaultDeviceSerial;
			}
		}
		set
		{
			lock (_gate)
			{
				_defaultDeviceSerial = value;
			}
		}
	}

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

	/// <summary>Attaches a device and raises <see cref="DeviceConnected" />. A device removed earlier comes back as the same
	/// instance.</summary>
	public FakeAndroidDevice AddDevice(
		string serial,
		AndroidDeviceState state = AndroidDeviceState.Online,
		AndroidDeviceInfo? info = null)
	{
		FakeAndroidDevice device;
		lock (_gate)
		{
			if (_devices.Any(existing => string.Equals(existing.Serial, serial, StringComparison.Ordinal)))
			{
				throw new InvalidOperationException($"A device with serial '{serial}' is already attached.");
			}

			// The same instance comes back on a reconnect, as IAndroidDevice promises.
			if (_known.TryGetValue(serial, out var known))
			{
				device = known;
				device.Reattach(info ?? new AndroidDeviceInfo(null, null, null), state);
			}
			else
			{
				device = new FakeAndroidDevice(this, serial, info ?? new AndroidDeviceInfo(null, null, null), state);
			}

			_devices.Add(device);
			_known[serial] = device;
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
			device = _devices.FirstOrDefault(existing
					=> string.Equals(existing.Serial, serial, StringComparison.Ordinal))
				?? throw new InvalidOperationException($"No device with serial '{serial}' is attached.");
		}

		var previous = device.State;
		if (previous == state)
		{
			return;
		}

		device.SetState(state);
		DeviceStateChanged?.Invoke(this, new AndroidDeviceEventArgs(device, previous));
	}

	/// <summary>Changes an attached device's <see cref="IAndroidDevice.LacksUsbPermission" /> and raises
	/// <see cref="DeviceStateChanged" />.</summary>
	public void SetLacksUsbPermission(string serial, bool lacksUsbPermission)
	{
		FakeAndroidDevice device;
		lock (_gate)
		{
			device = _devices.FirstOrDefault(existing
					=> string.Equals(existing.Serial, serial, StringComparison.Ordinal))
				?? throw new InvalidOperationException($"No device with serial '{serial}' is attached.");
		}

		if (device.LacksUsbPermission == lacksUsbPermission)
		{
			return;
		}

		device.SetLacksUsbPermission(lacksUsbPermission);
		DeviceStateChanged?.Invoke(this, new AndroidDeviceEventArgs(device, device.State));
	}

	/// <summary>Changes an attached device's <see cref="IAndroidDevice.Mode" /> and raises
	/// <see cref="DeviceStateChanged" />.</summary>
	public void SetDeviceMode(string serial, AndroidDeviceMode mode)
	{
		FakeAndroidDevice device;
		lock (_gate)
		{
			device = _devices.FirstOrDefault(existing
					=> string.Equals(existing.Serial, serial, StringComparison.Ordinal))
				?? throw new InvalidOperationException($"No device with serial '{serial}' is attached.");
		}

		if (device.Mode == mode)
		{
			return;
		}

		device.SetMode(mode);
		DeviceStateChanged?.Invoke(this, new AndroidDeviceEventArgs(device, device.State));
	}
}
