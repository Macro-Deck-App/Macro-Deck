namespace MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

public class CreateWidgetsResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
	public List<Widget>? Widgets { get; set; }
}
