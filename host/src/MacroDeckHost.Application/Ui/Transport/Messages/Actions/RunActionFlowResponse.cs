namespace MacroDeckHost.Application.Ui.Transport.Messages.Actions;

public class RunActionFlowResponse
{
	public bool Success { get; set; }

	public long DurationMs { get; set; }

	public TransportError? Error { get; set; }

	public string ExecutionId { get; set; } = string.Empty;
	public ActionExecutionStatus Status { get; set; }
	public List<ActionOutcomeDto> Actions { get; set; } = [];
}
