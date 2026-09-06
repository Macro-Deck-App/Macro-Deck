using MacroDeckHost.Application.Store.Model;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public class StoreAvailableUpdateBody
{
	public StoreExtensionKind Kind { get; set; }

	public string PackageId { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;

	public string InstalledVersion { get; set; } = string.Empty;

	public string LatestVersion { get; set; } = string.Empty;
}
