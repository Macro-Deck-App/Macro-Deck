namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public class RefreshStoreRegistryResponse
{
	public bool Success { get; set; }

	public StoreRegistryStatusBody Registry { get; set; } = new();

	public TransportError? Error { get; set; }
}
