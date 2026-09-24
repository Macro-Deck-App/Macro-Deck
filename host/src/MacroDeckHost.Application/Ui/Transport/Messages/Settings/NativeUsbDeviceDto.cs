using MacroDeckHost.Application.Usb;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Settings;

public class NativeUsbDeviceDto
{
	public string Id { get; set; } = string.Empty;

	public string Platform { get; set; } = string.Empty;

	public string? Serial { get; set; }

	public string? Manufacturer { get; set; }

	public string? Product { get; set; }

	public string State { get; set; } = string.Empty;

	public bool Picked { get; set; }

	public bool Remembered { get; set; }

	public bool CanConnect { get; set; }

	public bool KnownToAdb { get; set; }

	public static NativeUsbDeviceDto From(NativeUsbDevice device) => new()
	{
		Id = device.Id,
		Platform = device.Platform.ToString(),
		Serial = device.Serial,
		Manufacturer = device.Manufacturer,
		Product = device.Product,
		State = device.State.ToString(),
		Picked = device.Picked,
		Remembered = device.Remembered,
		KnownToAdb = device.KnownToAdb,
		CanConnect = device.State is NativeUsbDeviceState.Available or NativeUsbDeviceState.ServedByAdb or
			NativeUsbDeviceState.Stopped
	};
}
