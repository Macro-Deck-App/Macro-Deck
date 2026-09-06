namespace MacroDeckHost.Application.Ui.Transport.Messages.Folders;

public class CreateFolderResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
	public Folder? Folder { get; set; }
}
