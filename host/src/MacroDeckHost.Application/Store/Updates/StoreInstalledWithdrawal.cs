using MacroDeckHost.Application.Store.Model;

namespace MacroDeckHost.Application.Store.Updates;

public sealed record StoreInstalledWithdrawal
{
	public required StoreExtensionKind Kind { get; init; }

	public required string PackageId { get; init; }

	public required string Name { get; init; }

	public required string InstalledVersion { get; init; }

	public string? Reason { get; init; }

	public string? Replacement { get; init; }

	public bool Listed { get; init; }
}
