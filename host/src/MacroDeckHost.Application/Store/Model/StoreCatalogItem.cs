namespace MacroDeckHost.Application.Store.Model;

public sealed record StoreCatalogItem
{
	public required StoreCatalogEntry Entry { get; init; }

	public required StoreInstallState InstallState { get; init; }

	public string? InstalledVersion { get; init; }

	public string? InstalledTestBuild { get; init; }

	public Guid? ActiveOperationId { get; init; }

	public StoreTrustPresentation Trust { get; init; } = StoreTrustPresentation.RegistryAuthenticated;

	public string? UnsupportedReason { get; init; }

	public StoreRemovedPackage? Withdrawal { get; init; }

	public StoreRemovedPackage? InstalledVersionRemoval { get; init; }

	public IReadOnlyList<string> WithdrawnVersions { get; init; } = [];
}
