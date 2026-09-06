namespace MacroDeckHost.Application.Ui.Transport.Messages.Variables;

public class VariablesChangedEvent
{
	public List<Variable> Upserted { get; set; } = [];

	public List<string> DeletedIds { get; set; } = [];
}
