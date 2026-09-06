namespace MacroDeckHost.Application.Ui.Transport.Messages.Devices;

public class DeviceChangedEvent
{
	public Device Device { get; set; } = new();
}
