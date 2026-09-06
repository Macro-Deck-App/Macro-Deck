namespace MacroDeckHost.Application.Ui.Transport.Messages.Folders;

public class FolderFocusRuleChangedEvent
{
	public string FolderId { get; set; } = string.Empty;
	public List<FolderFocusRule> Rules { get; set; } = new();
}
