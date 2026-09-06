namespace MacroDeckHost.Application.Ui.Transport.Messages.Devices;

public class SetDeviceStartupProfileResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
	public Device? Device { get; set; }
}
