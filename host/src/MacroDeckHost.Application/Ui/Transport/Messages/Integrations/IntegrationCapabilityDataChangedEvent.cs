namespace MacroDeckHost.Application.Ui.Transport.Messages.Integrations;

public class IntegrationCapabilityDataChangedEvent
{
	public string Kind { get; set; } = string.Empty;

	public List<string> IntegrationIds { get; set; } = [];
}
