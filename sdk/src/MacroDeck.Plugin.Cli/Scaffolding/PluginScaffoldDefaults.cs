namespace MacroDeck.Plugin.Cli.Scaffolding;

internal static class PluginScaffoldDefaults
{
	/// <summary>Every RID <c>new</c> knows about, in the fixed order used both for the fallback numbered
	/// multi-select and for restricting/ordering a manifest's <c>entrypoints</c> and a build config's
	/// <c>targets</c> to the selection. <c>--platform</c> accepts any of these six.</summary>
	public static readonly IReadOnlyList<string> KnownPlatforms =
		["win-x64", "win-arm64", "osx-arm64", "osx-x64", "linux-x64", "linux-arm64"];

	/// <summary>The RIDs the wizard's checkbox prompt actually offers - narrower than
	/// <see cref="KnownPlatforms" /> because Macro Deck ships no builds for the other three RIDs yet, so
	/// offering them would scaffold a plugin for a host that does not exist. <c>--platform</c> still accepts
	/// all six <see cref="KnownPlatforms" />.</summary>
	public static readonly IReadOnlyList<string> OfferedPlatforms = ["win-x64", "osx-arm64", "linux-x64"];

	public static readonly IReadOnlyList<string> DefaultPlatforms = ["win-x64", "osx-arm64", "linux-x64"];

	public const string License = "MIT";

	public const string DefaultDescription = "A Macro Deck plugin.";

	public const string TemplatePackageId = "MacroDeck.Plugin.Templates";

	public const string TemplateShortName = "macrodeck-plugin";

	/// <summary>Defined by the reader that consumes it, so <c>new</c> and <c>build</c> can never drift on
	/// the name of the file one writes and the other reads.</summary>
	public const string BuildConfigFileName = Building.PluginBuildConfigReader.FileName;
}
