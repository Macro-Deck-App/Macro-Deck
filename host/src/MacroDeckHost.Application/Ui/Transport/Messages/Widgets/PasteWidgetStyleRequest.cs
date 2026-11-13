namespace MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

public class PasteWidgetStyleRequest
{
	public List<string> WidgetIds { get; set; } = [];

	public string Style { get; set; } = string.Empty;
}
