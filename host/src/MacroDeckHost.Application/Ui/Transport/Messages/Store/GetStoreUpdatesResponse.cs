namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public class GetStoreUpdatesResponse
{
	public List<StoreAvailableUpdateBody> Updates { get; set; } = [];
}
