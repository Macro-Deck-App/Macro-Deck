using MacroDeckHost.Application.Store.Model;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public class UninstallStoreExtensionRequest
{
	public StoreExtensionKind Kind { get; set; }

	public string Id { get; set; } = string.Empty;
}
