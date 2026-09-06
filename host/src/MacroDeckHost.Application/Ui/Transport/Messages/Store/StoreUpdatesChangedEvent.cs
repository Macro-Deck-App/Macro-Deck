namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public class StoreUpdatesChangedEvent
{
	public List<StoreAvailableUpdateBody> Updates { get; set; } = [];
}
