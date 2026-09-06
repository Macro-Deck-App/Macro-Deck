namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public class GetStoreExtensionResponse
{
	public StoreExtensionDetailBody? Extension { get; set; }

	public TransportError? Error { get; set; }
}
