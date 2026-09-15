using System.Text.Json;
using MacroDeck.Plugin.Cli.Manifests;
using MacroDeck.Plugin.Cli.Scaffolding;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Plugin.Packaging.Versioning;

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

	[TestCase("3.0.0-beta.1", true)]
	[TestCase("3.0.0-beta.6", true)]
	[TestCase("3.0.0-rc.1", true)]
	[TestCase("3.0.0", true)]
	[TestCase("3.1.0", true)]
	[TestCase("4.0.0", true)]
	[TestCase("2.9.9", false)]
	public void The_written_macro_deck_range_admits_3_0_prerelease_and_later_hosts(string hostVersion,
		bool expected)
	{
		var json = ScaffoldManifestWriter.BuildManifestJson(TemplateManifestJson, CanonicalRequest());
		using var document = JsonDocument.Parse(json);
		var written = document.RootElement.GetProperty("compatibility").GetProperty("macroDeck").GetString();

		Assert.That(SemanticVersionRange.TryParse(written, out var range), Is.True, written);
		Assert.That(SemanticVersion.TryParse(hostVersion, out var host), Is.True);
		Assert.That(range!.Satisfies(host!), Is.EqualTo(expected), $"'{written}' against host {hostVersion}");
	}

	[Test]
	public void No_protocol_compatibility_is_written()
	{
		var json = ScaffoldManifestWriter.BuildManifestJson(TemplateManifestJson, CanonicalRequest());
		using var document = JsonDocument.Parse(json);
		var compatibility = document.RootElement.GetProperty("compatibility");

		Assert.That(compatibility.TryGetProperty("protocol", out _), Is.False);
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
			Is.EqualTo("runtimes/win-x64/SpotifyCtl.dll"));
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
	public void Every_entrypoint_is_a_framework_dependent_dll_under_its_own_rid_slot()
	{
		var request = CanonicalRequest(platforms: PluginScaffoldDefaults.KnownPlatforms);
		var json = ScaffoldManifestWriter.BuildManifestJson(TemplateManifestJson, request);
		using var document = JsonDocument.Parse(json);
		var entrypoints = document.RootElement.GetProperty("entrypoints");

		Assert.That(entrypoints.EnumerateObject().Select(p => p.Name),
			Is.EquivalentTo(PluginScaffoldDefaults.KnownPlatforms));

		foreach (var property in entrypoints.EnumerateObject())
		{
			var entrypoint = property.Value;
			Assert.That(entrypoint.GetProperty("executable").GetString(),
				Is.EqualTo($"runtimes/{property.Name}/SpotifyController.dll"));

			var runtime = entrypoint.GetProperty("runtime");
			Assert.That(runtime.GetProperty("kind").GetString(), Is.EqualTo("FrameworkDependent"));
			Assert.That(runtime.GetProperty("dotnetVersion").GetString(), Is.EqualTo("10.0"));
		}
	}

	[Test]
	public void The_host_manifest_reader_accepts_every_entrypoint_as_framework_dependent_on_dotnet_10()
	{
		var request = CanonicalRequest(platforms: PluginScaffoldDefaults.KnownPlatforms);
		var json = ScaffoldManifestWriter.BuildManifestJson(TemplateManifestJson, request);

		var result = new PluginManifestReader().ReadFromJson(json, null, "com.example.spotify", "1.0.0");

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		Assert.That(result.Manifest!.Entrypoints.Keys, Is.EquivalentTo(PluginScaffoldDefaults.KnownPlatforms));

		foreach (var (_, entrypoint) in result.Manifest.Entrypoints)
		{
			Assert.That(entrypoint.Runtime?.Kind, Is.EqualTo(PluginEntrypointRuntimeKind.FrameworkDependent));
			Assert.That(entrypoint.Runtime?.DotnetVersion, Is.EqualTo("10.0"));
		}
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
