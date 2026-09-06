namespace MacroDeckHost.Application.Ui.Transport.Messages.Folders;

public class CreateFolderRequest
{
	public string ProfileId { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string? ParentId { get; set; }

	/// <summary>Which folder view the new folder uses. Absent is the built-in widget grid.</summary>
	public string? FolderViewId { get; set; }

	/// <summary>The view's configuration as JSON object text.</summary>
	public string? FolderViewConfiguration { get; set; }
}
