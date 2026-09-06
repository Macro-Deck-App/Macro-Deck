using System.Text.Json;
using System.Text.Json.Nodes;

namespace MacroDeck.Plugin.Cli.Scaffolding;

/// <summary>
/// Builds <c>macrodeck-build.json</c>, the developer build configuration <c>macrodeck-plugin build</c>
/// (#617) reads: structured <c>executable</c> + <c>arguments</c>, never an opaque shell command string, and
/// nothing .NET-specific about the shape - a Rust plugin's own build config would carry the same three
/// properties per target with a different <c>executable</c>. Publishes self-contained per RID because the
/// manifest schema forbids a <c>.dll</c> executable without a <c>runtime</c> block, and framework-dependent
/// output is RID-agnostic, which would leave nothing to separate the <c>runtimes/&lt;rid&gt;/</c> layout by.
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
					"true",
					"-o",
					output),
				["output"] = output
			};
		}

		var root = new JsonObject { ["version"] = 1, ["targets"] = targets };

		return root.ToJsonString(_writeOptions);
	}
}
