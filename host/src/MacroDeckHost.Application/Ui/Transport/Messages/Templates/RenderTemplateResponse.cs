namespace MacroDeckHost.Application.Ui.Transport.Messages.Templates;

public class RenderTemplateResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
	public string Rendered { get; set; } = string.Empty;
}
