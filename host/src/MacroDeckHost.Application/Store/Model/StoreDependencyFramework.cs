namespace MacroDeckHost.Application.Store.Model;

public sealed record StoreDependencyFramework
{
	public required string Framework { get; init; }

	public required IReadOnlyList<StoreDependencyPackage> Packages { get; init; }
}
