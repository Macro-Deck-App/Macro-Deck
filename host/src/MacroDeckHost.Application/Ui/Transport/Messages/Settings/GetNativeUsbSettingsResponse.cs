using MacroDeckHost.Application.Usb;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Settings;

public class GetNativeUsbSettingsResponse
{
	public bool Enabled { get; set; }

	public bool AndroidAvailable { get; set; }

	public bool IosAvailable { get; set; }

	public bool BridgeAvailable { get; set; }

	public bool HttpsOnly { get; set; }

	public IReadOnlyList<NativeUsbDeviceDto> Devices { get; set; } = [];

	public IReadOnlyList<NativeUsbRememberedDeviceDto> RememberedDevices { get; set; } = [];

	internal T Fill<T>(NativeUsbStatus status) where T : GetNativeUsbSettingsResponse
	{
		Enabled = status.Enabled;
		AndroidAvailable = status.AndroidAvailable;
		IosAvailable = status.IosAvailable;
		BridgeAvailable = status.BridgeAvailable;
		HttpsOnly = status.HttpsOnly;
		Devices = status.Devices.Select(NativeUsbDeviceDto.From).ToList();
		RememberedDevices = status.RememberedDevices
			.Select(device => new NativeUsbRememberedDeviceDto { Serial = device.Serial, Name = device.Name })
			.ToList();
		return (T)this;
	}
}
