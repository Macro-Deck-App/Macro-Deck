namespace MacroDeck.Plugin.Cli.Scaffolding;

/// <summary>The raw <c>new</c> command line, before defaults or derivations are applied - every member is
/// nullable so "the user typed it" stays distinguishable from "nothing was supplied".</summary>
internal sealed record PluginScaffoldInputs
{
	public string? Name { get; init; }

	public string? Id { get; init; }

	public string? Publisher { get; init; }

	public string? Description { get; init; }

	public string? Repository { get; init; }

	public string? Homepage { get; init; }

	public string? License { get; init; }

	public string? ProjectName { get; init; }

	public string? Output { get; init; }

	public IReadOnlyList<string>? Platforms { get; init; }

	public bool Yes { get; init; }

	public bool NonInteractive { get; init; }

	public string? TemplateVersion { get; init; }

	public bool SkipTemplateInstall { get; init; }

	public bool NoRestore { get; init; }
}
