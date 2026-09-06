using MacroDeck.Plugin.Cli.Scaffolding;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>Stands in for what the real plugin project template writes, for
/// <c>PluginScaffolderTests</c>/<c>NewCommandOptionTests</c>/<c>NewCommandWizardTests</c> - hand-written,
/// never produced through this repository's own scaffolding code.</summary>
internal static class ScaffoldFixtures
{
	public static void WriteTemplateProject(PluginScaffoldRequest request)
	{
		Directory.CreateDirectory(request.Output);
		File.WriteAllText(Path.Combine(request.Output, "manifest.json"), TemplateManifestJson(request));
	}

	public static string TemplateManifestJson(PluginScaffoldRequest request)
	{
		return $$"""
				 {
				 	"$schema": "https://schemas.macro-deck.app/plugin-manifest-v1.schema.json",
				 	"manifestVersion": 1,
				 	"id": "{{request.Id}}",
				 	"name": "{{request.Name}}",
				 	"version": "1.0.0",
				 	"entrypoints": {
				 		"win-x64": { "executable": "{{request.ProjectName}}.exe" }
				 	}
				 }
				 """;
	}
}
