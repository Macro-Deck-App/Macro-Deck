namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public class StoreRegistryStatusChangedEvent
{
	public StoreRegistryStatusBody Registry { get; set; } = new();
}
