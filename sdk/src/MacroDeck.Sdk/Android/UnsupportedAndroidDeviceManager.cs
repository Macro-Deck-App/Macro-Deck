namespace MacroDeck.Sdk.Android;

/// <summary>
/// The <see cref="IAndroidDeviceManager" /> of a context that offers no ADB: <see cref="Access" /> is always
/// <see cref="AndroidDeviceAccess.Unsupported" />, there are no devices, no event is ever raised, and
/// <see cref="ConnectAsync" /> throws <see cref="AndroidDeviceException" /> with
/// <see cref="AndroidDeviceErrorCode.Unsupported" />.
/// </summary>
public sealed class UnsupportedAndroidDeviceManager : IAndroidDeviceManager
{
	public static readonly UnsupportedAndroidDeviceManager Instance = new();

	private UnsupportedAndroidDeviceManager()
	{
	}

	public AndroidDeviceAccess Access => AndroidDeviceAccess.Unsupported;

	public IReadOnlyCollection<IAndroidDevice> Devices => [];

	public IAndroidDevice? FindDevice(string serial)
	{
		return null;
	}

	public Task<IAndroidDevice> ConnectAsync(string address, CancellationToken cancellationToken = default)
	{
		return Task.FromException<IAndroidDevice>(new AndroidDeviceException(AndroidDeviceErrorCode.Unsupported,
			"This context offers no ADB."));
	}

	public event EventHandler<AndroidDeviceEventArgs>? DeviceConnected
	{
		add { }
		remove { }
	}

	public event EventHandler<AndroidDeviceEventArgs>? DeviceDisconnected
	{
		add { }
		remove { }
	}

	public event EventHandler<AndroidDeviceEventArgs>? DeviceStateChanged
	{
		add { }
		remove { }
	}

	public event EventHandler? AccessChanged
	{
		add { }
		remove { }
	}
}
