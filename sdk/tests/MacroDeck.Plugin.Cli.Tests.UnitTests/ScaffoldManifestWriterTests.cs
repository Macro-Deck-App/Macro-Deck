using System.Text.Json;
using MacroDeck.Plugin.Cli.Manifests;
using MacroDeck.Plugin.Cli.Scaffolding;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

[TestFixture]
public class ScaffoldManifestWriterTests
{
	private const string TemplateManifestJson = """
												{
													"$schema": "https://schemas.macro-deck.app/plugin-manifest-v1.schema.json",
													"manifestVersion": 1,
													"id": "com.example.spotify",
													"name": "Spotify Controller",
													"version": "1.0.0",
													"icon": "assets/icon.png",
													"entrypoints": {
														"win-x64": { "executable": "SpotifyController.exe" }
													}
												}
												""";

	private static PluginScaffoldRequest CanonicalRequest(string description = "A Macro Deck plugin.",
		string? repository = null,
		string? homepage = null,
		string license = "MIT",
		IReadOnlyList<string>? platforms = null)
	{
		return new PluginScaffoldRequest
		{
			Name = "Spotify Controller",
			Id = "com.example.spotify",
			Publisher = "Example Publisher",
			Description = description,
			Repository = repository,
			Homepage = homepage,
			License = license,
			ProjectName = "SpotifyController",
			Output = "SpotifyController",
			Platforms = platforms ?? ["win-x64", "osx-arm64", "linux-x64"]
		};
	}

	[Test]
	public void Schema_and_document_identity_fields_survive_untouched()
	{
		var json = ScaffoldManifestWriter.BuildManifestJson(TemplateManifestJson, CanonicalRequest());
		using var document = JsonDocument.Parse(json);
		var root = document.RootElement;

		Assert.Multiple(() =>
		{
			Assert.That(root.GetProperty("$schema").GetString(),
				Is.EqualTo("https://schemas.macro-deck.app/plugin-manifest-v1.schema.json"));
			Assert.That(root.GetProperty("id").GetString(), Is.EqualTo("com.example.spotify"));
			Assert.That(root.GetProperty("name").GetString(), Is.EqualTo("Spotify Controller"));
			Assert.That(root.GetProperty("version").GetString(), Is.EqualTo("1.0.0"));
			Assert.That(root.GetProperty("icon").GetString(), Is.EqualTo("assets/icon.png"));
		});
	}

	[Test]
	public void Schema_key_is_the_first_property()
	{
		var json = ScaffoldManifestWriter.BuildManifestJson(TemplateManifestJson, CanonicalRequest());
		using var document = JsonDocument.Parse(json);

		var firstProperty = document.RootElement.EnumerateObject().First().Name;
		Assert.That(firstProperty, Is.EqualTo("$schema"));
	}

	// b1
	[Test]
	public void Omitted_optionals_are_absent_keys()
	{
		var json = ScaffoldManifestWriter.BuildManifestJson(TemplateManifestJson, CanonicalRequest());
		using var document = JsonDocument.Parse(json);
		var root = document.RootElement;

		Assert.Multiple(() =>
		{
			Assert.That(root.TryGetProperty("repository", out _), Is.False);
			Assert.That(root.TryGetProperty("homepage", out _), Is.False);
			Assert.That(root.TryGetProperty("shutdown", out _), Is.False);
			Assert.That(root.TryGetProperty("health", out _), Is.False);
			Assert.That(root.TryGetProperty("permissions", out _), Is.False);
			Assert.That(root.TryGetProperty("dependencies", out _), Is.False);
			Assert.That(root.TryGetProperty("conflicts", out _), Is.False);
			Assert.That(root.TryGetProperty("iconPacks", out _), Is.False);
			Assert.That(root.TryGetProperty("files", out _), Is.False);
			Assert.That(root.TryGetProperty("signature", out _), Is.False);

			Assert.That(root.GetProperty("license").GetString(), Is.EqualTo("MIT"));

			var publisher = root.GetProperty("publisher");
			Assert.That(publisher.GetProperty("name").GetString(), Is.EqualTo("Example Publisher"));
			Assert.That(publisher.TryGetProperty("id", out _), Is.False);
			Assert.That(publisher.TryGetProperty("email", out _), Is.False);
			Assert.That(publisher.TryGetProperty("url", out _), Is.False);
		});
	}

	[Test]
	public void An_open_ended_macro_deck_compatibility_range_is_always_written()
	{
		var json = ScaffoldManifestWriter.BuildManifestJson(TemplateManifestJson, CanonicalRequest());
		using var document = JsonDocument.Parse(json);
		var compatibility = document.RootElement.GetProperty("compatibility");

		Assert.Multiple(() =>
		{
			Assert.That(compatibility.GetProperty("macroDeck").GetString(), Is.EqualTo(">=3.0.0"));

			// Deliberately not 'protocol': a fixed {minimum:1,maximum:1} range would get the scaffolded
			// plugin rejected by a future protocol-2 host.
			Assert.That(compatibility.TryGetProperty("protocol", out _), Is.False);
		});
	}

	// b2
	[Test]
	public void Supplied_optionals_are_present_verbatim()
	{
		var request = CanonicalRequest("Controls Spotify playback.",
			"https://github.com/example/spotify",
			"https://example.com/spotify",
			"Apache-2.0");

		var json = ScaffoldManifestWriter.BuildManifestJson(TemplateManifestJson, request);
		using var document = JsonDocument.Parse(json);
		var root = document.RootElement;

		Assert.Multiple(() =>
		{
			Assert.That(root.GetProperty("description").GetString(), Is.EqualTo("Controls Spotify playback."));
			Assert.That(root.GetProperty("repository").GetString(), Is.EqualTo("https://github.com/example/spotify"));
			Assert.That(root.GetProperty("homepage").GetString(), Is.EqualTo("https://example.com/spotify"));
			Assert.That(root.GetProperty("license").GetString(), Is.EqualTo("Apache-2.0"));
			Assert.That(root.GetProperty("publisher").TryGetProperty("url", out _), Is.False);
		});
	}

	// b3
	[Test]
	public void The_generated_manifest_passes_schema_validation()
	{
		var json = ScaffoldManifestWriter.BuildManifestJson(TemplateManifestJson, CanonicalRequest());
		using var document = JsonDocument.Parse(json);

		var problems = PluginManifestSchema.Validate(document.RootElement);
		Assert.That(problems.Where(p => p.Severity == ManifestProblemSeverity.Error), Is.Empty);
	}

	// b5
	[Test]
	public void Project_name_propagates_into_entrypoints()
	{
		var request = CanonicalRequest() with { ProjectName = "SpotifyCtl" };
		var json = ScaffoldManifestWriter.BuildManifestJson(TemplateManifestJson, request);
		using var document = JsonDocument.Parse(json);

		var entrypoints = document.RootElement.GetProperty("entrypoints");
		Assert.That(entrypoints.GetProperty("win-x64").GetProperty("executable").GetString(),
			Is.EqualTo("runtimes/win-x64/SpotifyCtl.exe"));
	}

	// c1
	[Test]
	public void Only_selected_platforms_appear_as_entrypoints()
	{
		var request = CanonicalRequest(platforms: ["osx-arm64", "linux-arm64"]);
		var json = ScaffoldManifestWriter.BuildManifestJson(TemplateManifestJson, request);
		using var document = JsonDocument.Parse(json);

		var entrypoints = document.RootElement.GetProperty("entrypoints");
		var keys = entrypoints.EnumerateObject().Select(p => p.Name).ToList();

		Assert.That(keys, Is.EquivalentTo(new List<string> { "osx-arm64", "linux-arm64" }));
		Assert.That(json, Does.Not.Contain("win-x64"));
	}

	// c2 + c3
	[Test]
	public void Canonical_entrypoints_are_distinct_prefixed_and_carry_no_runtime_or_dll()
	{
		var json = ScaffoldManifestWriter.BuildManifestJson(TemplateManifestJson, CanonicalRequest());
		using var document = JsonDocument.Parse(json);
		var entrypoints = document.RootElement.GetProperty("entrypoints");

		var executables = new List<string>();

		foreach (var property in entrypoints.EnumerateObject())
		{
			var entrypoint = property.Value;
			var executable = entrypoint.GetProperty("executable").GetString()!;
			executables.Add(executable);

			Assert.That(entrypoint.TryGetProperty("runtime", out _), Is.False);
			Assert.That(executable.EndsWith(".dll", StringComparison.OrdinalIgnoreCase), Is.False);
			Assert.That(executable, Does.Not.EndsWith(".sh"));
			Assert.That(executable, Does.Not.EndsWith(".bat"));
			Assert.That(executable, Does.Not.EndsWith(".cmd"));
			Assert.That(executable, Does.Not.EndsWith(".ps1"));
			Assert.That(executable, Does.Not.EndsWith(".command"));
			Assert.That(executable, Does.StartWith($"runtimes/{property.Name}/"));
		}

		Assert.That(executables, Is.Unique);
		Assert.That(entrypoints.GetProperty("win-x64").GetProperty("executable").GetString(),
			Does.EndWith(".exe"));
		Assert.That(entrypoints.GetProperty("osx-arm64").GetProperty("executable").GetString(),
			Does.Not.Contain("."));
		Assert.That(entrypoints.GetProperty("linux-x64").GetProperty("executable").GetString(),
			Does.Not.Contain("."));
	}

	[Test]
	public void An_ampersand_in_a_url_round_trips_literally()
	{
		var request = CanonicalRequest(repository: "https://example.com/repo?x=1&y=2");
		var json = ScaffoldManifestWriter.BuildManifestJson(TemplateManifestJson, request);

		Assert.That(json, Does.Contain("https://example.com/repo?x=1&y=2"));

		using var document = JsonDocument.Parse(json);
		Assert.That(document.RootElement.GetProperty("repository").GetString(),
			Is.EqualTo("https://example.com/repo?x=1&y=2"));
	}
}
