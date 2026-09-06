namespace MacroDeckHost.Application.Ui.Transport.Messages.Devices;

public class SetDeviceStartupProfileRequest
{
	public Guid Id { get; set; }
	public string? ProfileId { get; set; }
}
