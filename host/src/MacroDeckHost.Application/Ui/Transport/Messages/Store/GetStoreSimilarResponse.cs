namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public class GetStoreSimilarResponse
{
	public List<StoreCatalogItemBody> Items { get; set; } = [];

	public TransportError? Error { get; set; }
}
