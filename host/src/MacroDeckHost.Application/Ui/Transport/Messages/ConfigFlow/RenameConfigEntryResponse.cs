namespace MacroDeckHost.Application.Ui.Transport.Messages.ConfigFlow;

public class RenameConfigEntryResponse
{
	public bool Success { get; set; }

	public TransportError? Error { get; set; }
}
