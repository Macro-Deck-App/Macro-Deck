using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Packaging.Tests.UnitTests.Manifest;

[TestFixture]
internal sealed class PluginBundledIconPackReaderTests
{
	private static readonly (string Key, string Path)[] _declared =
	[
		("logos", "icon-packs/logos.macroDeckIconPack"),
		("status-2", "assets/Status.MACRODECKICONPACK")
	];

	private string _root = null!;
	private string _versionDirectory = null!;

	[SetUp]
	public void SetUp()
	{
		_root = Path.Combine(Path.GetTempPath(), "md-bundled-icon-pack-tests-" + Guid.NewGuid().ToString("N"));
		_versionDirectory = Path.Combine(_root, "com.example.plugin", "versions", "1.0.0");
		Directory.CreateDirectory(_versionDirectory);
		File.WriteAllText(Path.Combine(_versionDirectory, "app"), "#!/bin/sh\n");
	}

	[TearDown]
	public void TearDown() => Directory.Delete(_root, recursive: true);

	[Test]
	public void Declared_bundled_packs_are_read_in_order_without_their_files_existing()
	{
		var result = Read("""
						  [
						  	{ "key": "logos", "path": "icon-packs/logos.macroDeckIconPack" },
						  	{ "key": "status-2", "path": "assets/Status.MACRODECKICONPACK" }
						  ]
						  """);

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		Assert.That(result.Manifest!.BundledIconPacks!.Select(pack => (pack.Key, pack.Path)), Is.EqualTo(_declared));
	}

	[TestCase("Logos")]
	[TestCase("-logos")]
	[TestCase("logos-")]
	[TestCase("lo gos")]
	[TestCase("logos/2")]
	[TestCase("")]
	public void An_invalid_key_is_rejected(string key)
	{
		var result = Read($$"""[ { "key": "{{key}}", "path": "icon-packs/logos.macroDeckIconPack" } ]""");

		AssertRejected(result);
	}

	[Test]
	public void A_key_longer_than_the_maximum_is_rejected_and_one_at_the_maximum_is_accepted()
	{
		var atMaximum = new string('a', PluginBundledIconPacks.MaxKeyLength);
		var tooLong = atMaximum + "a";

		var accepted = Read($$"""[ { "key": "{{atMaximum}}", "path": "p/a.macroDeckIconPack" } ]""");
		var rejected = Read($$"""[ { "key": "{{tooLong}}", "path": "p/a.macroDeckIconPack" } ]""");

		Assert.That(accepted.Success, Is.True, accepted.ErrorMessage);
		AssertRejected(rejected);
	}

	[TestCase("../outside.macroDeckIconPack")]
	[TestCase("/absolute/logos.macroDeckIconPack")]
	[TestCase("icon-packs\\\\logos.macroDeckIconPack")]
	[TestCase("icon-packs/logos.zip")]
	[TestCase("icon-packs/logos.macroDeckIconPack.bak")]
	public void An_unsafe_path_or_one_without_the_pack_extension_is_rejected(string path)
	{
		var result = Read($$"""[ { "key": "logos", "path": "{{path}}" } ]""");

		AssertRejected(result);
	}

	[Test]
	public void A_repeated_key_is_rejected()
	{
		var result = Read("""
						  [
						  	{ "key": "logos", "path": "icon-packs/a.macroDeckIconPack" },
						  	{ "key": "logos", "path": "icon-packs/b.macroDeckIconPack" }
						  ]
						  """);

		AssertRejected(result);
	}

	[Test]
	public void A_repeated_path_is_rejected_regardless_of_case()
	{
		var result = Read("""
						  [
						  	{ "key": "logos", "path": "icon-packs/logos.macroDeckIconPack" },
						  	{ "key": "brands", "path": "Icon-Packs/LOGOS.macroDeckIconPack" }
						  ]
						  """);

		AssertRejected(result);
	}

	[Test]
	public void More_than_the_maximum_number_of_packs_is_rejected_and_the_maximum_is_accepted()
	{
		var accepted = Read(Packs(PluginBundledIconPacks.MaxCount));
		var rejected = Read(Packs(PluginBundledIconPacks.MaxCount + 1));

		Assert.That(accepted.Success, Is.True, accepted.ErrorMessage);
		AssertRejected(rejected);
	}

	[Test]
	public void The_signed_digest_of_a_manifest_does_not_depend_on_its_bundled_packs()
	{
		const string Files = """
							 , "files": [
							 	{ "path": "icon-packs/logos.macroDeckIconPack", "sha256": "sha256:aa00000000000000000000000000000000000000000000000000000000000000", "size": 3 }
							 ]
							 """;

		var without = Read(null, Files);
		var with = Read("""[ { "key": "logos", "path": "icon-packs/logos.macroDeckIconPack" } ]""", Files);

		Assert.Multiple(() =>
		{
			Assert.That(with.Success, Is.True, with.ErrorMessage);
			Assert.That(without.Success, Is.True, without.ErrorMessage);
		});
		Assert.That(PluginArtifactDigest.Compute(with.Manifest!),
			Is.EqualTo(PluginArtifactDigest.Compute(without.Manifest!)));
	}

	private static string Packs(int count)
		=> "[" + string.Join(",",
			Enumerable.Range(0, count)
				.Select(index =>
					$$"""{ "key": "pack-{{index}}", "path": "icon-packs/pack-{{index}}.macroDeckIconPack" }""")) + "]";

	private PluginManifestReadResult Read(string? bundledIconPacks, string extra = "")
	{
		var declaration = bundledIconPacks is null ? string.Empty : ", \"bundledIconPacks\": " + bundledIconPacks;
		var path = Path.Combine(_versionDirectory, "manifest.json");

		File.WriteAllText(path,
			$$"""
			  {
			  	"manifestVersion": 1,
			  	"id": "com.example.plugin",
			  	"name": "Example",
			  	"version": "1.0.0",
			  	"entrypoints": { "{{PluginRuntimeIdentifiers.Current}}": { "executable": "app" } }
			  	{{declaration}}
			  	{{extra}}
			  }
			  """);

		return new PluginManifestReader().Read(path, "com.example.plugin", "1.0.0");
	}

	private static void AssertRejected(PluginManifestReadResult result)
	{
		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginManifestError.InvalidBundledIconPack));
		});
	}
}
