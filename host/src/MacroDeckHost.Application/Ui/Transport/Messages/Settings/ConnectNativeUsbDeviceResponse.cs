namespace MacroDeckHost.Application.Ui.Transport.Messages.Settings;

public class ConnectNativeUsbDeviceResponse : GetNativeUsbSettingsResponse
{
	public bool Success { get; set; }

	public string? ErrorCode { get; set; }
}
