namespace MacroDeckHost.Application.Ui.Transport.Messages.Actions;

public class ActionExecutionStatusEvent
{
	public string ExecutionId { get; set; } = string.Empty;
	public ActionExecutionStatus Status { get; set; }
	public long DurationMs { get; set; }
	public string? WidgetId { get; set; }
	public string? TriggerType { get; set; }
	public TransportError? Error { get; set; }
	public List<ActionOutcomeDto> Actions { get; set; } = [];
}
