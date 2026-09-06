namespace MacroDeckHost.Application.Ui.Transport.Messages.Automations;

public class AutomationUpdatedEvent
{
	public Automation Automation { get; set; } = new();
}
