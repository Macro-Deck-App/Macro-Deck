namespace MacroDeckHost.Application.Ui.Transport.Messages.Settings;

public class GetAutostartSettingsResponse
{
	public bool Supported { get; set; }

	public bool Enabled { get; set; }

	public bool OpenMinimized { get; set; }
}
