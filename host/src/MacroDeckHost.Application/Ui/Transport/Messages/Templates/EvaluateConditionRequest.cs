namespace MacroDeckHost.Application.Ui.Transport.Messages.Templates;

public class EvaluateConditionRequest
{
	public ConditionSide Left { get; set; } = new();
	public string Operator { get; set; } = "==";
	public ConditionSide Right { get; set; } = new();

	public string? Scope { get; set; }
	public string? ScopeRefId { get; set; }
}
