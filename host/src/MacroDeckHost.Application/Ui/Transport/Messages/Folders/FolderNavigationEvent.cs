namespace MacroDeckHost.Application.Ui.Transport.Messages.Folders;

public class FolderNavigationEvent
{
	public string Command { get; set; } = string.Empty;

	public string? FolderId { get; set; }

	public string? ProfileId { get; set; }

	public string? NavigationToken { get; set; }
}
