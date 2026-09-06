namespace MacroDeckHost.Application.Ui.Transport.Messages.Folders;

public class UpdateFolderResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
	public Folder? Folder { get; set; }
}
