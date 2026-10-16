namespace MacroDeckHost.Application.Store.Model;

public sealed record StoreDependencyPackage
{
	public required string Id { get; init; }

	public required string Version { get; init; }

	public string? RequestedVersion { get; init; }

	public bool Direct { get; init; }
}
