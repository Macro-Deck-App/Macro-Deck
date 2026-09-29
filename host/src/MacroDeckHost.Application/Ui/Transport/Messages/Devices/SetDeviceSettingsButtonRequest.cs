namespace MacroDeckHost.Application.Ui.Transport.Messages.Devices;

public class SetDeviceSettingsButtonRequest
{
	public Guid Id { get; set; }
	public bool Hidden { get; set; }
}
