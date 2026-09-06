namespace MacroDeckHost.Application.Ui.Transport.Messages.Scripts;

public class ScriptCreatedEvent
{
	public Script Script { get; set; } = new();
}
