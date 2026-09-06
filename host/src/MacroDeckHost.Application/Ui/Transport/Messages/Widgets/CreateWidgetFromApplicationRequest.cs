namespace MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

public class CreateWidgetFromApplicationRequest
{
	public string FolderId { get; set; } = string.Empty;

	public int PositionX { get; set; }

	public int PositionY { get; set; }

	public string Path { get; set; } = string.Empty;
}
