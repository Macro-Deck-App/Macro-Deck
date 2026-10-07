namespace MacroDeckHost.Application.Ui.Transport.Messages.Templates;

public class RenderTemplateRequest
{
	public string Template { get; set; } = string.Empty;

	public string? Scope { get; set; }

	public string? ScopeRefId { get; set; }

	public string? ResultType { get; set; }

	public int? DecimalPlaces { get; set; }

	public string? VariableId { get; set; }

	public string? VariableName { get; set; }
}
