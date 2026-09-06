namespace MacroDeckHost.Application.Ui.Transport.Messages.Folders;

public class SetFolderFocusRuleResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
	public FolderFocusRule? Rule { get; set; }
}
