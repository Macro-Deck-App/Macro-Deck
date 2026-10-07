using MacroDeckHost.Application.Ui.Transport.Messages.Variables;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Templates;

public class RenderTemplateResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
	public string Rendered { get; set; } = string.Empty;

	public string? Value { get; set; }

	public VariableTemplateErrorDto? TemplateError { get; set; }
}
