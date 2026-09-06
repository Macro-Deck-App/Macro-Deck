namespace MacroDeckHost.Application.Ui.Transport.Messages.Devices;

public class LogoutDeviceResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
}
