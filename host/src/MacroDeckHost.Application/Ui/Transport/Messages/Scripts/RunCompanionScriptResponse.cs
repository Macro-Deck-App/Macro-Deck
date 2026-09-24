using MacroDeckHost.Application.Ui.Transport.Messages.Actions;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Scripts;

public class RunCompanionScriptResponse
{
	public bool Success { get; set; }

	public ActionExecutionStatus Status { get; set; }

	public TransportError? Error { get; set; }
}
