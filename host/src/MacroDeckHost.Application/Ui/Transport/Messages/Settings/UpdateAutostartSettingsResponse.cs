namespace MacroDeckHost.Application.Ui.Transport.Messages.Settings;

public class UpdateAutostartSettingsResponse
{
	public bool Success { get; set; }

	public string? Error { get; set; }

	public bool Supported { get; set; }

	public bool Enabled { get; set; }

	public bool OpenMinimized { get; set; }
}
