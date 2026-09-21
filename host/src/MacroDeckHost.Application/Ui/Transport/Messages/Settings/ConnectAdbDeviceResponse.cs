namespace MacroDeckHost.Application.Ui.Transport.Messages.Settings;

public class ConnectAdbDeviceResponse : GetAdbSettingsResponse
{
	public bool Success { get; set; }

	public string? Error { get; set; }

	public string? ErrorCode { get; set; }
}
