namespace MacroDeckHost.Application.Ui.Transport.Messages.Integrations;

public class SetIntegrationEnabledRequest
{
	public string Id { get; set; } = string.Empty;

	public bool Enabled { get; set; }
}
