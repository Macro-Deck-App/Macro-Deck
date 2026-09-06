namespace MacroDeckHost.Application.Ui.Transport.Messages.Automations;

public class AutomationCreatedEvent
{
	public Automation Automation { get; set; } = new();
}
