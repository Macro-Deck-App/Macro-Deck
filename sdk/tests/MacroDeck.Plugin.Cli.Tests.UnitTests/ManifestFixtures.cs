using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>
/// Hand-written manifest fixtures - never generated through <c>MacroDeck.Plugin.Cli</c>'s own reader or
/// writer code, so a test built from one can never merely confirm the code agrees with itself.
/// </summary>
internal static class ManifestFixtures
{
	public const string PluginId = "com.example.test-plugin";

	public const string Version = "1.0.0";

	/// <summary>The declared entrypoint's file name. No extension: <c>PluginManifestReader</c> rejects an
	/// executable ending in <c>.dll</c> for a self-contained entrypoint (the default, absent "runtime"),
	/// and requires nothing else about the name.</summary>
	public const string EntrypointFileName = "placeholder";

	/// <summary>The id <see cref="WriteInvalidIdManifestDirectory" /> declares - spaces and capitals, which
	/// no reverse-domain id allows. Named so a test can assert on how often it is reported back.</summary>
	public const string InvalidPluginId = "Not A Valid Id";

	/// <summary>
	/// Writes a source tree with a manifest.json that <c>IPluginManifestReader</c> and the embedded schema
	/// both accept cleanly: a valid reverse-domain id (schema <c>reverseDomainId</c> pattern:
	/// lowercase, dot-separated, at least two segments - see plugin-hosting.md's own
	/// <c>"com.example.plugin"</c> example), and one entrypoint for whichever RID this test happens to run
	/// on, pointing at a real file next to the manifest so the reader's own existence check passes.
	/// </summary>
	public static string WriteValidManifestDirectory()
	{
		var directory = Directory.CreateTempSubdirectory("macrodeck-plugin-cli-tests-").FullName;
		File.WriteAllText(Path.Combine(directory, EntrypointFileName), string.Empty);
		File.WriteAllText(Path.Combine(directory, PluginArtifactFiles.ManifestFileName), ValidManifestJson());
		return directory;
	}

	/// <summary>
	/// A manifest whose <c>id</c> violates the one grammar rule every reverse-domain id in this codebase
	/// shares (lowercase, dot-separated segments - see <c>docs/src/content/docs/guides/hosting.md</c>'s
	/// own <c>"com.example.plugin"</c> example): spaces and capital letters, neither of which any
	/// reverse-domain id allows.
	/// </summary>
	public static string WriteInvalidIdManifestDirectory()
	{
		var directory = Directory.CreateTempSubdirectory("macrodeck-plugin-cli-tests-").FullName;
		File.WriteAllText(Path.Combine(directory, EntrypointFileName), string.Empty);

		var json = $$"""
					 {
					 	"manifestVersion": 1,
					 	"id": "{{InvalidPluginId}}",
					 	"name": "Test Plugin",
					 	"version": "{{Version}}",
					 	"entrypoints": {
					 		"{{PluginRuntimeIdentifiers.Current}}": { "executable": "{{EntrypointFileName}}" }
					 	}
					 }
					 """;

		File.WriteAllText(Path.Combine(directory, PluginArtifactFiles.ManifestFileName), json);
		return directory;
	}

	public static string ValidManifestJson()
	{
		return $$"""
				 {
				 	"manifestVersion": 1,
				 	"id": "{{PluginId}}",
				 	"name": "Test Plugin",
				 	"version": "{{Version}}",
				 	"entrypoints": {
				 		"{{PluginRuntimeIdentifiers.Current}}": { "executable": "{{EntrypointFileName}}" }
				 	}
				 }
				 """;
	}

	/// <summary>RIDs guaranteed not to be existence-checked by <c>IPluginManifestReader</c> for this test
	/// run: neither the running RID itself, nor a <c>PluginRuntimeIdentifiers.CandidatesFor</c> fallback of
	/// it (only <c>osx-arm64</c>-&gt;<c>osx-x64</c> and <c>win-arm64</c>-&gt;<c>win-x64</c> exist). Declaring
	/// an entrypoint for one of these lets a test control whether its backing file exists without the real
	/// reader ever failing validation over it - only <see cref="EntrypointPresence" /> (pack/inspect's own,
	/// separate check) looks at these.
	/// </summary>
	private static readonly string[] _ridPool =
		["linux-x64", "linux-arm64", "win-x64", "osx-arm64", "osx-x64", "win-arm64"];

	public static IReadOnlyList<string> PickForeignRids(int count)
	{
		var excluded = new HashSet<string>(PluginRuntimeIdentifiers.CandidatesFor(PluginRuntimeIdentifiers.Current),
			StringComparer.Ordinal);

		return _ridPool.Where(rid => !excluded.Contains(rid)).Take(count).ToList();
	}

	/// <summary>A valid manifest declaring the current RID (backing file always written) plus
	/// <paramref name="foreignRids" /> (see <see cref="PickForeignRids" />), each backed by a file only when
	/// <paramref name="createForeignFiles" /> is true - the knob
	/// <c>EntrypointPresenceTests</c>/<c>PluginPackerTests</c> use to produce a source tree with some,
	/// all, or none of its declared entrypoints actually present.</summary>
	public static string WriteMultiRidManifestDirectory(IReadOnlyList<string> foreignRids, bool createForeignFiles)
	{
		var directory = Directory.CreateTempSubdirectory("macrodeck-plugin-cli-tests-").FullName;
		File.WriteAllText(Path.Combine(directory, EntrypointFileName), string.Empty);

		var entrypoints = new List<string>
		{
			$$"""		"{{PluginRuntimeIdentifiers.Current}}": { "executable": "{{EntrypointFileName}}" }"""
		};

		foreach (var rid in foreignRids)
		{
			var fileName = $"placeholder-{rid}";

			if (createForeignFiles)
			{
				File.WriteAllText(Path.Combine(directory, fileName), string.Empty);
			}

			entrypoints.Add($$"""		"{{rid}}": { "executable": "{{fileName}}" }""");
		}

		var json = $$"""
					 {
					 	"manifestVersion": 1,
					 	"id": "{{PluginId}}",
					 	"name": "Test Plugin",
					 	"version": "{{Version}}",
					 	"entrypoints": {
					 {{string.Join(",\n", entrypoints)}}
					 	}
					 }
					 """;

		File.WriteAllText(Path.Combine(directory, PluginArtifactFiles.ManifestFileName), json);
		return directory;
	}

	/// <summary>Four independent manifest defects in one document - the issue's own example: an invalid id
	/// (spaces/capitals, same grammar violation as <see cref="WriteInvalidIdManifestDirectory" />), an empty
	/// name (schema <c>minLength: 1</c>), a non-SemVer version, and no entrypoints at all (schema
	/// <c>minProperties: 1</c>). No entrypoint file is written - none is declared.</summary>
	public static string WriteFourIndependentProblemsManifestDirectory()
	{
		var directory = Directory.CreateTempSubdirectory("macrodeck-plugin-cli-tests-").FullName;

		var json = """
				   {
				   	"manifestVersion": 1,
				   	"id": "NOT VALID ID!!",
				   	"name": "",
				   	"version": "not-a-version",
				   	"entrypoints": {}
				   }
				   """;

		File.WriteAllText(Path.Combine(directory, PluginArtifactFiles.ManifestFileName), json);
		return directory;
	}

	/// <summary>A manifest with a real SemVer <paramref name="version" /> substituted in for
	/// <see cref="Version" /> - otherwise identical to <see cref="ValidManifestJson" /> - to check that
	/// <c>validate</c> never flags a legitimate SemVer string as invalid.</summary>
	/// <summary>A manifest that is wrong in exactly one place - an unsupported <c>manifestVersion</c> - while
	/// declaring a perfectly legal entrypoint whose file exists. The combination matters: every other
	/// invalid fixture here declares no entrypoints, so nothing else exercises what the schema layer reports
	/// about a *valid* part of an otherwise invalid document.</summary>
	public static string WriteUnsupportedManifestVersionDirectory()
	{
		var directory = Directory.CreateTempSubdirectory("macrodeck-plugin-cli-tests-").FullName;
		File.WriteAllText(Path.Combine(directory, EntrypointFileName), string.Empty);

		var json = $$"""
					 {
					 	"manifestVersion": 2,
					 	"id": "{{PluginId}}",
					 	"name": "Test Plugin",
					 	"version": "{{Version}}",
					 	"entrypoints": {
					 		"{{PluginRuntimeIdentifiers.Current}}": { "executable": "{{EntrypointFileName}}" }
					 	}
					 }
					 """;

		File.WriteAllText(Path.Combine(directory, PluginArtifactFiles.ManifestFileName), json);
		return directory;
	}

	public static string WriteManifestDirectoryWithVersion(string version)
	{
		var directory = Directory.CreateTempSubdirectory("macrodeck-plugin-cli-tests-").FullName;
		File.WriteAllText(Path.Combine(directory, EntrypointFileName), string.Empty);

		var json = $$"""
					 {
					 	"manifestVersion": 1,
					 	"id": "{{PluginId}}",
					 	"name": "Test Plugin",
					 	"version": "{{version}}",
					 	"entrypoints": {
					 		"{{PluginRuntimeIdentifiers.Current}}": { "executable": "{{EntrypointFileName}}" }
					 	}
					 }
					 """;

		File.WriteAllText(Path.Combine(directory, PluginArtifactFiles.ManifestFileName), json);
		return directory;
	}

	/// <summary>A manifest with <paramref name="name" /> substituted in for <see cref="ValidManifestJson" />'s
	/// <c>"Test Plugin"</c> - used to check the schema's <c>name.maxLength: 128</c> against the reader's
	/// own bound, JSON-escaped so a name of arbitrary length still embeds safely.</summary>
	public static string WriteManifestDirectoryWithName(string name)
	{
		var directory = Directory.CreateTempSubdirectory("macrodeck-plugin-cli-tests-").FullName;
		File.WriteAllText(Path.Combine(directory, EntrypointFileName), string.Empty);

		var json = $$"""
					 {
					 	"manifestVersion": 1,
					 	"id": "{{PluginId}}",
					 	"name": {{System.Text.Json.JsonSerializer.Serialize(name)}},
					 	"version": "{{Version}}",
					 	"entrypoints": {
					 		"{{PluginRuntimeIdentifiers.Current}}": { "executable": "{{EntrypointFileName}}" }
					 	}
					 }
					 """;

		File.WriteAllText(Path.Combine(directory, PluginArtifactFiles.ManifestFileName), json);
		return directory;
	}

	/// <summary>JSON broken on an early line (line 2: an unquoted, unterminated string value) - the file
	/// never parses at all, so <c>IPluginManifestReader</c> reports <c>Malformed</c>.</summary>
	public static string WriteEarlyMalformedManifestDirectory()
	{
		var directory = Directory.CreateTempSubdirectory("macrodeck-plugin-cli-tests-").FullName;

		var json = """
				   {
				   	"manifestVersion": 1,
				   	"id: "com.example.broken",
				   	"name": "Broken Plugin",
				   	"version": "1.0.0",
				   	"entrypoints": {}
				   }
				   """;

		File.WriteAllText(Path.Combine(directory, PluginArtifactFiles.ManifestFileName), json);
		return directory;
	}

	/// <summary>An otherwise-plausible ~10 line manifest document truncated mid-object, so parsing fails at
	/// end-of-file rather than on an early line - the counterexample <c>DescribeMalformedJson</c> exists
	/// for: end-of-file is never line 1.</summary>
	public static string WriteUnterminatedMalformedManifestDirectory()
	{
		var directory = Directory.CreateTempSubdirectory("macrodeck-plugin-cli-tests-").FullName;

		var json = """
				   {
				   	"manifestVersion": 1,
				   	"id": "com.example.broken",
				   	"name": "Broken Plugin",
				   	"version": "1.0.0",
				   	"description": "Cut off before this object closes",
				   	"entrypoints": {
				   		"linux-x64": { "executable": "placeholder"
				   """;

		File.WriteAllText(Path.Combine(directory, PluginArtifactFiles.ManifestFileName), json);
		return directory;
	}

	/// <summary>A manifest next to a <c>*.csproj</c>, declaring the current RID's entrypoint pointing at a
	/// file that does not exist - the exact "validated the source tree instead of the build output" mix-up
	/// issue #556 calls out. <see cref="ManifestValidator" /> is expected to name it a source directory
	/// rather than a broken manifest.</summary>
	public static string WriteSourceDirectoryManifestDirectory()
	{
		var directory = Directory.CreateTempSubdirectory("macrodeck-plugin-cli-tests-").FullName;
		File.WriteAllText(Path.Combine(directory, "PluginTemplate.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
		File.WriteAllText(Path.Combine(directory, PluginArtifactFiles.ManifestFileName), ValidManifestJson());
		return directory;
	}

	/// <summary>As <see cref="WriteSourceDirectoryManifestDirectory" /> - a missing current-RID entrypoint -
	/// but with no project file next to the manifest, so nothing suggests this is a source tree. The
	/// counterexample <c>A_build_output_directory_with_a_missing_entrypoint_is_not_called_a_source_directory</c>
	/// depends on this staying a plain build-output directory that merely lost a binary.</summary>
	public static string WriteMissingEntrypointManifestDirectory()
	{
		var directory = Directory.CreateTempSubdirectory("macrodeck-plugin-cli-tests-").FullName;
		File.WriteAllText(Path.Combine(directory, PluginArtifactFiles.ManifestFileName), ValidManifestJson());
		return directory;
	}

	/// <summary>A manifest with <c>shutdown.gracefulTimeoutSeconds</c> set to <paramref name="seconds" /> -
	/// used to check that <c>ManifestPeek</c> clamps an out-of-range value the same way the real reader's
	/// <c>ClampSettings</c> does (docs/src/content/docs/guides/packaging.md: "clamped 1-60s").</summary>
	public static string WriteManifestDirectoryWithGracePeriod(int seconds)
	{
		var directory = Directory.CreateTempSubdirectory("macrodeck-plugin-cli-tests-").FullName;
		File.WriteAllText(Path.Combine(directory, EntrypointFileName), string.Empty);

		var json = $$"""
					 {
					 	"manifestVersion": 1,
					 	"id": "{{PluginId}}",
					 	"name": "Test Plugin",
					 	"version": "{{Version}}",
					 	"shutdown": { "gracefulTimeoutSeconds": {{seconds}} },
					 	"entrypoints": {
					 		"{{PluginRuntimeIdentifiers.Current}}": { "executable": "{{EntrypointFileName}}" }
					 	}
					 }
					 """;

		File.WriteAllText(Path.Combine(directory, PluginArtifactFiles.ManifestFileName), json);
		return directory;
	}
}
