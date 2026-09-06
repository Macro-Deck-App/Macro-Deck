namespace MacroDeckHost.Application.Ui.Transport.Messages.Folders;

public class DeleteFolderResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
}
