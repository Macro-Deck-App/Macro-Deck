using MacroDeckHost.Application.Services;

namespace MacroDeckHost.Application.Usb;

public enum NativeUsbPlatform
{
	Android,

	Ios
}

public enum NativeUsbDeviceState
{
	Available,

	ServedByAdb,

	Waiting,

	WaitingForApp,

	Linked,

	Closed,

	Stopped,

	NotSupported,

	Connecting,

	Switching
}

public sealed record NativeUsbDevice(
	string Id,
	NativeUsbPlatform Platform,
	string? Serial,
	string? Manufacturer,
	string? Product,
	NativeUsbDeviceState State,
	bool Picked,
	bool Remembered,
	bool KnownToAdb = false);

public sealed record NativeUsbStatus(
	bool Enabled,
	bool AndroidAvailable,
	bool IosAvailable,
	bool BridgeAvailable,
	bool HttpsOnly,
	IReadOnlyList<NativeUsbDevice> Devices,
	IReadOnlyList<RememberedUsbDevice> RememberedDevices)
{
	public static readonly NativeUsbStatus Initial = new(false, false, false, false, false, [], []);
}

public enum NativeUsbPickResult
{
	Picked,

	UnknownDevice,

	NotAllowed
}

public interface INativeUsbManager
{
	NativeUsbStatus Status { get; }

	Task ApplySettingsAsync(CancellationToken cancellationToken);

	Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken);

	Task PollAsync(CancellationToken cancellationToken);

	Task<NativeUsbPickResult> PickAsync(string deviceId, CancellationToken cancellationToken);

	Task<bool> ForgetAsync(string serial, CancellationToken cancellationToken);

	Task ShutdownAsync(CancellationToken cancellationToken);
}
