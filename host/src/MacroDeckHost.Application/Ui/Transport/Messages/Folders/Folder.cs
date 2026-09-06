using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Folders;

public class Folder
{
	public string Id { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string? ProfileId { get; set; }
	public string? ParentId { get; set; }
	public int Order { get; set; }

	public int? Rows { get; set; }

	public int? Columns { get; set; }

	public string? BackgroundColor { get; set; }

	public int? WidgetSpacing { get; set; }

	public int? WidgetBorderRadius { get; set; }

	public bool IsDefault { get; set; }

	/// <summary>The folder view rendering this folder. Always written, never absent, so a client never has
	/// to decide what "no view" means.</summary>
	public string ViewId { get; set; } = string.Empty;

	/// <summary>The view's configuration as stored JSON text. Absent when the view has none.</summary>
	public string? ViewConfiguration { get; set; }

	public List<Widget> Widgets { get; set; } = new();
}
