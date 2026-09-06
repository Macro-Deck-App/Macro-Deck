namespace MacroDeckHost.Application.Ui.Transport.Messages.Scripts;

public class DuplicateScriptResponse
{
	public bool Success { get; set; }

	public Script? Script { get; set; }

	public TransportError? Error { get; set; }
}
