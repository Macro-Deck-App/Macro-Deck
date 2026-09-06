namespace MacroDeckHost.Application.Ui.Transport.Messages.Devices;

public class RenameDeviceRequest
{
	public Guid Id { get; set; }
	public string Name { get; set; } = string.Empty;
}
