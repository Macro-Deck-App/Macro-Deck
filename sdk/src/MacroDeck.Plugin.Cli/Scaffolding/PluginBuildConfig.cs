using System.Text.Json;
using System.Text.Json.Nodes;

namespace MacroDeck.Plugin.Cli.Scaffolding;

/// <summary>
/// Builds <c>macrodeck-build.json</c>, the developer build configuration <c>macrodeck-plugin build</c>
/// (#617) reads: structured <c>executable</c> + <c>arguments</c>, never an opaque shell command string, and
/// nothing .NET-specific about the shape - a Rust plugin's own build config would carry the same three
/// properties per target with a different <c>executable</c>. Publishes framework-dependent, because the host
/// ships the .NET runtime plugins run on, but still per RID so each <c>runtimes/&lt;rid&gt;/</c> slot carries
/// only that RID's native assets.
/// </summary>
internal static class PluginBuildConfig
{
	private static readonly JsonSerializerOptions _writeOptions = new() { WriteIndented = true };

	public static string Build(PluginScaffoldRequest request)
	{
		var targets = new JsonObject();

		foreach (var rid in PluginScaffoldDefaults.KnownPlatforms)
		{
			if (!request.Platforms.Contains(rid, StringComparer.Ordinal))
			{
				continue;
			}

			var output = $"bin/publish/{rid}";

			targets[rid] = new JsonObject
			{
				["executable"] = "dotnet",
				["arguments"] = new JsonArray("publish",
					$"{request.ProjectName}.csproj",
					"-c",
					"Release",
					"-r",
					rid,
					"--self-contained",
					"false",
					"-p:UseAppHost=false",
					"-o",
					output),
				["output"] = output
			};
		}

		var root = new JsonObject { ["version"] = 1, ["targets"] = targets };

		return root.ToJsonString(_writeOptions);
	}
}
