namespace MacroDeck.Plugin.Cli.Scaffolding;

/// <summary>A complete, already-validated <c>new</c> request - what both the flag-only path and the
/// wizard converge on, so <see cref="PluginScaffolder" /> never has to know which one produced it.</summary>
internal sealed record PluginScaffoldRequest
{
	public required string Name { get; init; }

	public required string Id { get; init; }

	public required string Publisher { get; init; }

	public required string Description { get; init; }

	public string? Repository { get; init; }

	public string? Homepage { get; init; }

	public required string License { get; init; }

	public required string ProjectName { get; init; }

	public required string Output { get; init; }

	/// <summary>Always non-empty and always in <see cref="PluginScaffoldDefaults.KnownPlatforms" /> order.</summary>
	public required IReadOnlyList<string> Platforms { get; init; }

	public string? TemplateVersion { get; init; }

	public bool SkipTemplateInstall { get; init; }

	public bool NoRestore { get; init; }
}
