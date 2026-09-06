namespace MacroDeck.Plugin.Cli.Building;

internal sealed record PluginBuildRequest
{
	public required string SourceDirectory { get; init; }

	public required string ManifestPath { get; init; }

	public required string BuildConfigPath { get; init; }

	/// <summary>Build only this runtime identifier. Null builds every runtime identifier the manifest
	/// declares.</summary>
	public string? Rid { get; init; }

	public required string OutputDirectory { get; init; }

	public bool Force { get; init; }

	/// <summary>Where the staging tree is created. Null means the system temporary directory. Never inside
	/// the source tree, so a build can never leave artefacts in a repository.</summary>
	public string? StagingRoot { get; init; }
}
