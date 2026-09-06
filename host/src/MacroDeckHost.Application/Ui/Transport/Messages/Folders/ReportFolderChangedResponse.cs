namespace MacroDeckHost.Application.Ui.Transport.Messages.Folders;

public class ReportFolderChangedResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
}
