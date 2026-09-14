namespace MacroDeckHost.Application.Ui.Transport.Messages.Devices;

public class SetDeviceScreenSaverResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
	public Device? Device { get; set; }
}
