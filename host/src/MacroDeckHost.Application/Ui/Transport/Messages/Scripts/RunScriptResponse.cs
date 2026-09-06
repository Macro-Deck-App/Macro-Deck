using MacroDeckHost.Application.Ui.Transport.Messages.Actions;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Scripts;

public class RunScriptResponse
{
	public bool Success { get; set; }

	public TransportError? Error { get; set; }

	public string ExecutionId { get; set; } = string.Empty;
	public ActionExecutionStatus Status { get; set; }
	public long DurationMs { get; set; }
	public List<ActionOutcomeDto> Actions { get; set; } = [];

	/// <summary>The declared input names this run actually applied a value for.</summary>
	public List<string> AppliedInputs { get; set; } = [];
}
