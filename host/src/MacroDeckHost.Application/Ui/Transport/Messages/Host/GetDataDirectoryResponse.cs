namespace MacroDeckHost.Application.Ui.Transport.Messages.Host;

public class GetDataDirectoryResponse
{
	public string Path { get; set; } = string.Empty;

	public bool CanOpen { get; set; }
}
