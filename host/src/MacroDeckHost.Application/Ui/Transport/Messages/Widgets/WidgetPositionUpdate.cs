namespace MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

public class WidgetPositionUpdate
{
	public string Id { get; set; } = string.Empty;
	public int PositionX { get; set; }
	public int PositionY { get; set; }
	public int Width { get; set; }
	public int Height { get; set; }
}
