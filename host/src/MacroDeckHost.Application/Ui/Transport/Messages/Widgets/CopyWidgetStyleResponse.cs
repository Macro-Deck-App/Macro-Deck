namespace MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

public class CopyWidgetStyleResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
	public string? Style { get; set; }
}
