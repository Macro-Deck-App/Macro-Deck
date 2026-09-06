namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public class UninstallStoreExtensionResponse
{
	public bool Success { get; set; }

	public TransportError? Error { get; set; }
}
