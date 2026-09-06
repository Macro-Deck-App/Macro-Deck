namespace MacroDeckHost.Application.Ui.Transport.Messages.Folders;

public class FolderPlacement
{
	public string Id { get; set; } = string.Empty;
	public string? ParentId { get; set; }
	public int Order { get; set; }
	public bool IsDefault { get; set; }
}
