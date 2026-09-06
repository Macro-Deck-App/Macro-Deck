namespace MacroDeckHost.Application.Ui.Transport.Messages.Integrations;

public class SetIntegrationEnabledResponse
{
	public bool Success { get; set; }

	public TransportError? Error { get; set; }
}
