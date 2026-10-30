using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace MacroDeckHost.Tests.SystemTests.Support;

// Hand-packed rather than produced by the CLI: a package built by the code under test would only prove
// the reader agrees with itself.
internal static class FixtureArtifacts
{
	public const string WellBehavedPluginId = "app.macro-deck.well-behaved-test-plugin";

	public const string MisbehavingPluginId = "app.macro-deck.misbehaving-test-plugin";

	public static string WellBehaved(string targetDirectory, string? version = null)
	{
		return Pack(BuildOutputs.WellBehavedBuildDirectory(),
			BuildOutputs.WellBehavedAssemblyName,
			targetDirectory,
			version);
	}

	public static string Misbehaving(string targetDirectory, string? version = null)
	{
		return Pack(BuildOutputs.MisbehavingBuildDirectory(),
			BuildOutputs.MisbehavingAssemblyName,
			targetDirectory,
			version);
	}

	// The fixture's own manifest, with its apphost entrypoints replaced by the framework-dependent
	// assembly for this machine, which is how a cross-platform package without a RID build ships.
	private static string Pack(string buildDirectory, string assemblyName, string targetDirectory, string? version)
	{
		var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(buildDirectory, "manifest.json")))!.AsObject();
		manifest.Remove("$schema");
		if (version is not null)
		{
			manifest["version"] = version;
		}

		manifest["entrypoints"] = new JsonObject
		{
			[RuntimeInformation.RuntimeIdentifier] = new JsonObject
			{
				["executable"] = assemblyName + ".dll",
				["runtime"] = new JsonObject { ["kind"] = "FrameworkDependent", ["dotnetVersion"] = "10.0" }
			}
		};

		Directory.CreateDirectory(targetDirectory);
		var artifact = Path.Combine(targetDirectory,
			$"{assemblyName}-{manifest["version"]!.GetValue<string>()}-{Guid.NewGuid():N}.macroDeckPlugin");

		using var archive = ZipFile.Open(artifact, ZipArchiveMode.Create);
		using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open()))
		{
			writer.Write(manifest.ToJsonString());
		}

		foreach (var file in Directory.EnumerateFiles(buildDirectory, "*", SearchOption.AllDirectories))
		{
			var relative = Path.GetRelativePath(buildDirectory, file).Replace(Path.DirectorySeparatorChar, '/');
			if (!string.Equals(relative, "manifest.json", StringComparison.OrdinalIgnoreCase))
			{
				archive.CreateEntryFromFile(file, relative);
			}
		}

		return artifact;
	}
}
