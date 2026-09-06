namespace MacroDeckHost.Application.Ui.Transport.Messages.Folders;

public class MoveFolderResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
	public List<FolderPlacement>? Folders { get; set; }
}
