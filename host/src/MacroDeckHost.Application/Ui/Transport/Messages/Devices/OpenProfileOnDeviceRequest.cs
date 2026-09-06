namespace MacroDeckHost.Application.Ui.Transport.Messages.Devices;

public class OpenProfileOnDeviceRequest
{
	public Guid Id { get; set; }
	public string ProfileId { get; set; } = string.Empty;
}
