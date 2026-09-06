namespace MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

public class UpdateWidgetPositionsRequest
{
	public string FolderId { get; set; } = string.Empty;
	public List<WidgetPositionUpdate> Positions { get; set; } = [];
}
