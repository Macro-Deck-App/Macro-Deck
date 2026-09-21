namespace MacroDeck.Sdk.Android;

/// <summary>
/// The Android devices Macro Deck's own adb sees. Resolve it from dependency injection. Macro Deck owns
/// discovery, connection and lifetime: a plugin never creates, connects or disposes a device.
/// </summary>
/// <remarks>
/// A plugin may use ADB only when ADB is enabled in Macro Deck, the user allows plugins to use it, and the
/// plugin declares the <c>host:adb</c> permission in its manifest. <see cref="Access" /> says which of these
/// holds. Members added in later versions arrive as default interface members.
/// </remarks>
public interface IAndroidDeviceManager
{
	/// <summary>
	/// Whether this plugin may use ADB right now. <see cref="AndroidDeviceAccess.Unsupported" /> until the host
	/// has reported it, and permanently on a host that predates this API.
	/// </summary>
	AndroidDeviceAccess Access { get; }

	/// <summary>
	/// The attached devices, in any state but gone. Empty unless <see cref="Access" /> is
	/// <see cref="AndroidDeviceAccess.Available" />.
	/// </summary>
	IReadOnlyCollection<IAndroidDevice> Devices { get; }

	/// <summary>The attached device with this serial, or <c>null</c>.</summary>
	IAndroidDevice? FindDevice(string serial);

	/// <summary>
	/// Connects Macro Deck's adb to a device with wireless debugging on, like <c>adb connect</c>.
	/// <paramref name="address" /> is <c>host:port</c> with a host name or IPv4 address, for example
	/// <c>192.168.1.20:5555</c>, and becomes the device's serial. Connecting an already connected device succeeds.
	/// </summary>
	/// <returns>
	/// The device. It stays in <see cref="Devices" /> for every plugin until it disconnects; its
	/// <see cref="IAndroidDevice.State" /> can briefly be <see cref="AndroidDeviceState.Offline" /> before
	/// Macro Deck reports it.
	/// </returns>
	/// <exception cref="AndroidDeviceException">
	/// The same codes as a device operation; <see cref="AndroidDeviceErrorCode.CommandFailed" /> when adb could not
	/// reach the address, <see cref="AndroidDeviceErrorCode.InvalidArgument" /> for a malformed address, and
	/// <see cref="AndroidDeviceErrorCode.HostLocked" /> while Macro Deck is locked.
	/// </exception>
	Task<IAndroidDevice> ConnectAsync(string address, CancellationToken cancellationToken = default);

	/// <summary>Raised when a device appears. In a plugin, handlers run on the connection's receive loop: keep them short.</summary>
	event EventHandler<AndroidDeviceEventArgs>? DeviceConnected;

	/// <summary>Raised when a device goes away, including when access to ADB is withdrawn.</summary>
	event EventHandler<AndroidDeviceEventArgs>? DeviceDisconnected;

	/// <summary>Raised when an attached device's <see cref="IAndroidDevice.State" /> changes.</summary>
	event EventHandler<AndroidDeviceEventArgs>? DeviceStateChanged;

	/// <summary>Raised when <see cref="Access" /> changes.</summary>
	event EventHandler? AccessChanged;
}
