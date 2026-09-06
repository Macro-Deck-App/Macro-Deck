namespace MacroDeckHost.Application.Ui.Transport.Messages.Secrets;

public class UpdateSecretResponse
{
	public bool Success { get; set; }

	public TransportError? Error { get; set; }
}
