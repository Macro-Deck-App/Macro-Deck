using MacroDeckHost.Application.Adb;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Settings;

public class AdbDeviceDto
{
	public string Serial { get; set; } = string.Empty;

	public string State { get; set; } = string.Empty;

	public string? Model { get; set; }

	public string? Manufacturer { get; set; }

	public bool Authorized { get; set; }

	public bool IsDefault { get; set; }

	public bool TunnelEstablished { get; set; }

	public int? TunnelDevicePort { get; set; }

	public string? TunnelError { get; set; }

	public static AdbDeviceDto From(AdbDevice device, string? defaultDeviceSerial) => new()
	{
		Serial = device.Serial,
		State = device.State.ToString(),
		Model = device.Model,
		Manufacturer = device.Manufacturer,
		Authorized = device.IsAuthorized,
		IsDefault = string.Equals(device.Serial, defaultDeviceSerial, StringComparison.Ordinal),
		TunnelEstablished = device.Tunnel?.Established ?? false,
		TunnelDevicePort = device.Tunnel?.DevicePort,
		TunnelError = device.Tunnel?.FailureMessage
	};
}
