namespace MacroDeckHost.Application.Ui.Transport.Messages.Actions;

public class RunActionFlowRequest
{
	public string Flows { get; set; } = string.Empty;

	public string TriggerId { get; set; } = string.Empty;

	public string? Scope { get; set; }

	public string? ScopeRefId { get; set; }

	public string? ClientId { get; set; }
}
