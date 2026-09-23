using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Packaging.Tests.UnitTests.Manifest;

[TestFixture]
internal sealed class PackageAiDeclarationTests
{
	private static readonly string[] _declaredServices = ["OpenAI", "ElevenLabs"];

	private static readonly string[] _keptServices = ["OpenAI", "Mistral"];

	private static readonly string[] _singleService = ["OpenAI"];

	private static readonly JsonSerializerOptions _packOptions =
		new(PluginManifestJson.Options) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

	private static PluginManifestReadResult Read(string extra)
		=> new PluginManifestReader().ReadFromJson($$"""
			{
				"manifestVersion": 1,
				"id": "com.example.plugin",
				"name": "Example",
				"version": "1.0.0",
				"entrypoints": { "win-x64": { "executable": "Example.exe" } }
				{{extra}}
			}
			""",
			versionDirectory: null,
			"com.example.plugin",
			"1.0.0");

	[Test]
	public void A_manifest_without_a_declaration_declares_nothing()
	{
		var result = Read(string.Empty);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Manifest!.Ai, Is.Null);
		});
	}

	[Test]
	public void A_declaration_is_read_as_authored()
	{
		var result = Read("""
			, "ai": {
				"interaction": true,
				"generatedContent": false,
				"generatedAssets": true,
				"services": ["OpenAI", "ElevenLabs"]
			}
			""");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Manifest!.Ai!.Interaction, Is.True);
			Assert.That(result.Manifest.Ai.GeneratedContent, Is.False);
			Assert.That(result.Manifest.Ai.GeneratedAssets, Is.True);
			Assert.That(result.Manifest.Ai.Services, Is.EqualTo(_declaredServices));
		});
	}

	[Test]
	public void An_explicit_declaration_of_no_ai_stays_distinct_from_no_declaration()
	{
		var result = Read("""
			, "ai": { "interaction": false, "generatedContent": false, "generatedAssets": false }
			""");

		Assert.Multiple(() =>
		{
			Assert.That(result.Manifest!.Ai, Is.Not.Null);
			Assert.That(result.Manifest.Ai!.Interaction || result.Manifest.Ai.GeneratedContent ||
				result.Manifest.Ai.GeneratedAssets, Is.False);
		});
	}

	[TestCase("\"yes\"")]
	[TestCase("[true]")]
	[TestCase("42")]
	[TestCase("null")]
	public void A_declaration_that_is_not_an_object_never_stops_the_plugin_from_loading(string value)
	{
		var result = Read($", \"ai\": {value}");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.Manifest!.Ai, Is.Null);
		});
	}

	[TestCase("\"interaction\": \"true\"")]
	[TestCase("\"generatedContent\": 1")]
	[TestCase("\"generatedAssets\": {}")]
	[TestCase("\"interaction\": null")]
	[TestCase("\"services\": \"OpenAI\"")]
	[TestCase("\"services\": [\"\"]")]
	[TestCase("\"services\": [7]")]
	public void An_unreadable_member_leaves_the_package_undeclared_instead_of_claiming_no_ai(string flag)
	{
		var result = Read($", \"ai\": {{ {flag} }}");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.Manifest!.Ai, Is.Null);
		});
	}

	[Test]
	public void Malformed_services_are_dropped_without_touching_the_flags()
	{
		var tooLong = new string('x', PackageAiDeclaration.MaxServiceLength + 1);
		var result = Read($$"""
			, "ai": {
				"generatedAssets": true,
				"interaction": false,
				"services": ["  OpenAI  ", "", 7, "openai", "{{tooLong}}", "Mistral"],
				"futureFlag": true
			}
			""");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.Manifest!.Ai!.Interaction, Is.False);
			Assert.That(result.Manifest.Ai.GeneratedAssets, Is.True);
			Assert.That(result.Manifest.Ai.Services, Is.EqualTo(_keptServices));
		});
	}

	[Test]
	public void A_single_service_name_that_is_too_long_leaves_the_package_undeclared()
	{
		var tooLong = new string('x', PackageAiDeclaration.MaxServiceLength + 1);

		var result = Read($", \"ai\": {{ \"services\": [\"{tooLong}\"] }}");

		Assert.That(result.Manifest!.Ai, Is.Null);
	}

	[Test]
	public void An_empty_service_list_is_a_readable_declaration()
	{
		var result = Read(", \"ai\": { \"generatedAssets\": false, \"services\": [] }");

		Assert.Multiple(() =>
		{
			Assert.That(result.Manifest!.Ai, Is.Not.Null);
			Assert.That(result.Manifest.Ai!.Services, Is.Null);
		});
	}

	[Test]
	public void Services_are_capped()
	{
		var names = string.Join(", ",
			Enumerable.Range(0, PackageAiDeclaration.MaxServices + 4).Select(index => $"\"Service {index}\""));
		var result = Read($", \"ai\": {{ \"services\": [{names}] }}");

		Assert.That(result.Manifest!.Ai!.Services, Has.Count.EqualTo(PackageAiDeclaration.MaxServices));
	}

	[Test]
	public void A_packed_manifest_keeps_the_declaration_in_the_published_shape()
	{
		var manifest = Read("""
			, "ai": { "generatedContent": true, "services": ["OpenAI"] }
			""").Manifest!;
		using var written = JsonDocument.Parse(JsonSerializer.Serialize(manifest, _packOptions));
		var ai = written.RootElement.GetProperty("ai");
		var reread = Read($", \"ai\": {ai.GetRawText()}").Manifest!.Ai!;

		Assert.Multiple(() =>
		{
			Assert.That(ai.GetProperty("interaction").GetBoolean(), Is.False);
			Assert.That(ai.GetProperty("generatedContent").GetBoolean(), Is.True);
			Assert.That(ai.GetProperty("generatedAssets").GetBoolean(), Is.False);
			Assert.That(reread.GeneratedContent, Is.True);
			Assert.That(reread.Services, Is.EqualTo(_singleService));
		});
	}

	[Test]
	public void A_packed_manifest_without_a_declaration_omits_it()
	{
		var manifest = Read(string.Empty).Manifest!;
		using var written = JsonDocument.Parse(JsonSerializer.Serialize(manifest, _packOptions));

		Assert.That(written.RootElement.TryGetProperty("ai", out _), Is.False);
	}

	[Test]
	public void A_consumer_that_predates_the_declaration_still_reads_the_manifest()
	{
		const string json = """
			{
				"manifestVersion": 1,
				"id": "com.example.plugin",
				"name": "Example",
				"version": "1.0.0",
				"ai": { "interaction": true, "services": ["OpenAI"] }
			}
			""";

		var manifest = JsonSerializer.Deserialize<ManifestBeforeAiDeclaration>(json, PluginManifestJson.Options);

		Assert.That(manifest, Is.EqualTo(new ManifestBeforeAiDeclaration(1, "com.example.plugin", "Example", "1.0.0")));
	}

	private sealed record ManifestBeforeAiDeclaration(int ManifestVersion, string Id, string Name, string Version);
}
