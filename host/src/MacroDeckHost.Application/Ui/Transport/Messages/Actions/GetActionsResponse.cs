namespace MacroDeckHost.Application.Ui.Transport.Messages.Actions;

public class GetActionsResponse
{
	public List<ActionDefinition> Actions { get; set; } = new();
}
