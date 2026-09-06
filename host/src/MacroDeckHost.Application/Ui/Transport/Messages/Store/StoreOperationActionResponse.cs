namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public class StoreOperationActionResponse
{
	public bool Success { get; set; }

	public StoreOperationBody? Operation { get; set; }

	public TransportError? Error { get; set; }
}
