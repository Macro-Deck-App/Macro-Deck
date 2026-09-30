using MacroDeckHost.Application.Store.Model;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public class ResolveStoreLinkResponse
{
	public StoreExtensionKind? Kind { get; set; }

	public string? Id { get; set; }

	public TransportError? Error { get; set; }
}
