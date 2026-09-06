namespace MacroDeckHost.Application.Ui.Transport.Messages.Folders;

public class FoldersReorderedEvent
{
	public string ProfileId { get; set; } = string.Empty;
	public List<FolderPlacement> Folders { get; set; } = [];
}
