using System.Text.Json;
using System.Text.Json.Nodes;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>
/// Hand-written plugin projects for <c>build</c>: a <c>manifest.json</c> and a <c>macrodeck-build.json</c>
/// written as literal JSON rather than through the CLI's own writer or reader, so a test can never confirm
/// the implementation against itself.
/// </summary>
internal static class BuildFixtures
{
	public const string PluginId = "com.example.build-fixture";

	public const string Version = "1.0.0";

	public const string ProjectName = "FixturePlugin";

	/// <summary>Writes a buildable project declaring <paramref name="rids" /> in both the manifest and the
	/// build config, with the <c>runtimes/&lt;rid&gt;/</c> entrypoint layout publication expects, an
	/// <c>assets/icon.png</c>, a <c>README.md</c>, and <c>bin/</c>/<c>obj/</c> noise that must never be
	/// packaged.</summary>
	public static string WriteProject(IReadOnlyList<string> rids)
	{
		var directory = Directory.CreateTempSubdirectory("macrodeck-plugin-build-tests-").FullName;

		File.WriteAllText(Path.Combine(directory, "manifest.json"), ManifestJson(rids));
		File.WriteAllText(Path.Combine(directory, "macrodeck-build.json"), BuildConfigJson(rids));

		Directory.CreateDirectory(Path.Combine(directory, "assets"));
		File.WriteAllText(Path.Combine(directory, "assets", "icon.png"), "icon bytes");
		File.WriteAllText(Path.Combine(directory, "README.md"), "readme");

		Directory.CreateDirectory(Path.Combine(directory, "bin"));
		File.WriteAllText(Path.Combine(directory, "bin", "intermediate.txt"), "should not be packaged");
		Directory.CreateDirectory(Path.Combine(directory, "obj"));
		File.WriteAllText(Path.Combine(directory, "obj", "project.assets.json"), "should not be packaged");

		return directory;
	}

	public static string ExecutableName(string rid)
		=> rid.StartsWith("win-", StringComparison.Ordinal) ? $"{ProjectName}.exe" : ProjectName;

	public static string EntrypointPath(string rid) => $"runtimes/{rid}/{ExecutableName(rid)}";

	public static string OutputDirectory(string rid) => Path.Combine("bin", "publish", rid);

	/// <summary>Stands in for what a real build tool writes: the declared executable plus a sibling file,
	/// since self-contained output is never a single file.</summary>
	public static void WriteBuildOutput(string projectDirectory, string rid, string? executableContent = null)
	{
		var output = Path.Combine(projectDirectory, OutputDirectory(rid));
		Directory.CreateDirectory(output);

		File.WriteAllText(Path.Combine(output, ExecutableName(rid)), executableContent ?? $"executable for {rid}");
		File.WriteAllText(Path.Combine(output, $"{ProjectName}.deps.json"), $"deps for {rid}");
	}

	/// <summary>The runner callback that makes every configured target "succeed" by writing the output a
	/// real tool would have produced. The runner is told the RID through the argument vector the fixture's
	/// build config carries.</summary>
	public static Action<string, IReadOnlyList<string>, string> ProducingOutput(string projectDirectory,
		IReadOnlyList<string> rids)
	{
		return (_, arguments, _) =>
		{
			foreach (var rid in rids.Where(rid => arguments.Contains(rid, StringComparer.Ordinal)))
			{
				WriteBuildOutput(projectDirectory, rid);
			}
		};
	}

	public static string ManifestJson(IReadOnlyList<string> rids)
	{
		var entrypoints = new JsonObject();

		foreach (var rid in rids)
		{
			entrypoints[rid] = new JsonObject { ["executable"] = EntrypointPath(rid) };
		}

		var manifest = new JsonObject
		{
			["manifestVersion"] = 1,
			["id"] = PluginId,
			["name"] = "Fixture Plugin",
			["version"] = Version,
			["icon"] = "assets/icon.png",
			["entrypoints"] = entrypoints
		};

		return manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
	}

	public static string BuildConfigJson(IReadOnlyList<string> rids, IReadOnlyList<string>? include = null)
	{
		var targets = new JsonObject();

		foreach (var rid in rids)
		{
			targets[rid] = new JsonObject
			{
				["executable"] = "fixture-build-tool",
				["arguments"] = new JsonArray("publish", rid),
				["output"] = $"bin/publish/{rid}"
			};
		}

		var config = new JsonObject { ["version"] = 1, ["targets"] = targets };

		if (include is not null)
		{
			config["include"] = new JsonArray(include.Select(path => (JsonNode?)JsonValue.Create(path)).ToArray());
		}

		return config.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
	}
}
