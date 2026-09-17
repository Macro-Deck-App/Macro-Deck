namespace MacroDeckHost.Application.Store.Testing;

// A plugin's version directory says which version is installed but not which build of it, and a test build
// of a version can be followed by another build of that same version.
public sealed record StoreTestInstallationRecord
{
	public required string PluginId { get; init; }

	public required string Version { get; init; }

	public required string Build { get; init; }

	public required Guid BuildId { get; init; }

	public required string ArtifactSha256 { get; init; }

	public DateTimeOffset InstalledAt { get; init; }
}
