namespace MacroDeckHost.Application.Ui.Transport.Messages.Version;

public class GetVersionResponse
{
	public string Version { get; set; } = string.Empty;

	public bool IsBeta { get; set; }
}
