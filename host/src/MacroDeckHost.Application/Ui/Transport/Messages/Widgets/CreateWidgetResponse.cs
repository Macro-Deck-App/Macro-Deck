namespace MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

public class CreateWidgetResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
	public Widget? Widget { get; set; }
}
