namespace MacroDeckHost.Application.Ui.Transport.Messages.ConfigFlow;

public class DeleteConfigEntryResponse
{
	public bool Success { get; set; }

	public TransportError? Error { get; set; }
}
