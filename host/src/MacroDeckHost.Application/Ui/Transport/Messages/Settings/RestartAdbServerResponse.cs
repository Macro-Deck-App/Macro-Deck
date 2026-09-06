namespace MacroDeckHost.Application.Ui.Transport.Messages.Settings;

public class RestartAdbServerResponse : GetAdbSettingsResponse
{
	public bool Success { get; set; }

	public string? Error { get; set; }
}
