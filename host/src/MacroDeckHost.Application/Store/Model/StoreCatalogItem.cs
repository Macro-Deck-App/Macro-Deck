namespace MacroDeckHost.Application.Store.Model;

public sealed record StoreCatalogItem
{
	public required StoreCatalogEntry Entry { get; init; }

	public required StoreInstallState InstallState { get; init; }

	public string? InstalledVersion { get; init; }

	public Guid? ActiveOperationId { get; init; }

	public StoreTrustPresentation Trust { get; init; } = StoreTrustPresentation.RegistryAuthenticated;

	public string? UnsupportedReason { get; init; }
}
