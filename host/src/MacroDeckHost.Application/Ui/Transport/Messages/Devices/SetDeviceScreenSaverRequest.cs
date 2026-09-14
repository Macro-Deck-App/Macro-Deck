namespace MacroDeckHost.Application.Ui.Transport.Messages.Devices;

public class SetDeviceScreenSaverRequest
{
	public Guid Id { get; set; }
	public bool Enabled { get; set; }
	public int IdleSeconds { get; set; }
	public string? ScreenSaverId { get; set; }
	public string? Configuration { get; set; }
}
