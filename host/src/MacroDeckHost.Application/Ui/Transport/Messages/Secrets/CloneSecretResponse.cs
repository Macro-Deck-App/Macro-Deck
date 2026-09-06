namespace MacroDeckHost.Application.Ui.Transport.Messages.Secrets;

public class CloneSecretResponse
{
	public Guid? Id { get; set; }

	public TransportError? Error { get; set; }
}
