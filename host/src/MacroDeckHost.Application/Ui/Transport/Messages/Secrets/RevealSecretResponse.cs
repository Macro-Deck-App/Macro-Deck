namespace MacroDeckHost.Application.Ui.Transport.Messages.Secrets;

public class RevealSecretResponse
{
	public string? Value { get; set; }

	public TransportError? Error { get; set; }
}
