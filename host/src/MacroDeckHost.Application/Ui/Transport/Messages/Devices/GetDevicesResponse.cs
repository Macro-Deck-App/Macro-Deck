namespace MacroDeckHost.Application.Ui.Transport.Messages.Devices;

public class GetDevicesResponse
{
	public List<Device> Devices { get; set; } = new();
}
