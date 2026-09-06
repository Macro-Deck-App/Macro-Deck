using MacroDeckHost.Application.Store.Model;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public class StoreCatalogItemBody
{
	public StoreExtensionKind Kind { get; set; }

	public string Id { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;

	public string? Description { get; set; }

	public string? Publisher { get; set; }

	public string LatestVersion { get; set; } = string.Empty;

	public DateTimeOffset? CreatedAt { get; set; }

	public DateTimeOffset? UpdatedAt { get; set; }

	public StoreInstallState InstallState { get; set; }

	public string? InstalledVersion { get; set; }

	/// <summary>Why the host considers this package unsupported here, naming the runtime identifier it
	/// checked. Diagnostic only and never localized - a client that wants to say this to a reader uses
	/// its own <c>Store.NotSupportedOnPlatform</c> wording instead.</summary>
	public string? UnsupportedReason { get; set; }

	public StoreTrustPresentation Trust { get; set; }

	public bool HasIcon { get; set; }

	public Guid? ActiveOperationId { get; set; }
}
