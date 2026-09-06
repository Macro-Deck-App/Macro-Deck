using System.Text.Json;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Templates;

public class EvaluateExpressionRequest
{
	public JsonElement Expression { get; set; }

	public string? Scope { get; set; }
	public string? ScopeRefId { get; set; }

	public string? EventId { get; set; }
}
