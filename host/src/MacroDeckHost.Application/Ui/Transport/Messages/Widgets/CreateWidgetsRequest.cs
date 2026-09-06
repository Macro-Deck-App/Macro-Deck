using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

public class CreateWidgetsRequest
{
	public string FolderId { get; set; } = string.Empty;

	public List<CreateWidgetItem> Widgets { get; set; } = [];

	public List<string>? ReplaceIds { get; set; }
}

public class CreateWidgetItem
{
	public string Type { get; set; } = WidgetTypeIds.ActionButton;

	public int PositionX { get; set; }
	public int PositionY { get; set; }
	public int Width { get; set; }
	public int Height { get; set; }
	public string? Data { get; set; }
	public string? SourceWidgetId { get; set; }
}
