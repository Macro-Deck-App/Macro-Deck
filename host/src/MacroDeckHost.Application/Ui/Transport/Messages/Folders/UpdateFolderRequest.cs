namespace MacroDeckHost.Application.Ui.Transport.Messages.Folders;

public class UpdateFolderRequest
{
	public string Id { get; set; } = string.Empty;
	public string? Name { get; set; }
	public string? ParentId { get; set; }
	public int? Order { get; set; }

	public int? Rows { get; set; }

	public int? Columns { get; set; }

	public string? BackgroundColor { get; set; }

	public int? WidgetSpacing { get; set; }

	public int? WidgetBorderRadius { get; set; }

	public bool? IsDefault { get; set; }

	/// <summary>Absent leaves the view unchanged. Switching views drops the previous view's
	/// configuration unless <see cref="FolderViewConfiguration" /> supplies a new one.</summary>
	public string? FolderViewId { get; set; }

	/// <summary>Absent leaves the configuration unchanged.</summary>
	public string? FolderViewConfiguration { get; set; }
}
