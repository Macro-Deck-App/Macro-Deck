namespace MacroDeckHost.Application.Ui.Transport.Messages.Integrations;

public class GetIntegrationsResponse
{
	public List<Integration> Integrations { get; set; } = new();
}
