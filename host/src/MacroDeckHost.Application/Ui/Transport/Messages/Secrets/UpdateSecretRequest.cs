namespace MacroDeckHost.Application.Ui.Transport.Messages.Secrets;

public class UpdateSecretRequest
{
	public Guid Id { get; set; }

	public string Value { get; set; } = string.Empty;
}
