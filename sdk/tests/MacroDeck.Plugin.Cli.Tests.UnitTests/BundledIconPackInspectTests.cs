using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Plugin.Cli.Packing;
using MacroDeck.Plugin.Packaging.Artifacts;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

[TestFixture]
public class BundledIconPackInspectTests
{
	private string _directory = null!;
	private string _output = null!;

	[SetUp]
	public void SetUp()
	{
		_directory = ManifestFixtures.WriteValidManifestDirectory();
		_output = Directory.CreateTempSubdirectory("macrodeck-plugin-bundled-inspect-").FullName;

		IconPackFixtures.Write(Path.Combine(_directory, "icon-packs", "logos.macroDeckIconPack"),
			"Logos",
			["spotify", "discord"]);

		var manifestPath = Path.Combine(_directory, PluginArtifactFiles.ManifestFileName);
		var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
		manifest["bundledIconPacks"] = new JsonArray(
			new JsonObject { ["key"] = "logos", ["path"] = "icon-packs/logos.macroDeckIconPack" },
			new JsonObject { ["key"] = "gone", ["path"] = "icon-packs/gone.macroDeckIconPack" });
		File.WriteAllText(manifestPath, manifest.ToJsonString());
	}

	[TearDown]
	public void TearDown()
	{
		Directory.Delete(_directory, recursive: true);
		Directory.Delete(_output, recursive: true);
	}

	[Test]
	public async Task Inspect_lists_bundled_packs_of_a_directory_with_their_icon_counts_and_warns_about_a_missing_one()
	{
		var text = await CliRunner.Run("inspect", "--directory", _directory, "--no-color");
		var json = await CliRunner.Run("inspect", "--directory", _directory, "--output", "json", "--no-color");

		using var document = JsonDocument.Parse(json.Output);
		var packs = document.RootElement.GetProperty("bundledIconPacks");

		Assert.Multiple(() =>
		{
			Assert.That(text.ExitCode, Is.EqualTo(ExitCode.Success), text.Error);
			Assert.That(text.Output, Does.Contain("logos: icon-packs/logos.macroDeckIconPack - Logos, 2 icon(s)"));
			Assert.That(text.Output, Does.Contain("gone: icon-packs/gone.macroDeckIconPack - missing"));
			Assert.That(text.Error, Does.Contain("warning bundled-icon-pack-missing:").And.Contain("'gone'"));
			Assert.That(packs[0].GetProperty("key").GetString(), Is.EqualTo("logos"));
			Assert.That(packs[0].GetProperty("name").GetString(), Is.EqualTo("Logos"));
			Assert.That(packs[0].GetProperty("iconCount").GetInt32(), Is.EqualTo(2));
			Assert.That(packs[1].GetProperty("present").GetBoolean(), Is.False);
		});
	}

	[Test]
	public async Task Inspect_reads_bundled_packs_nested_in_an_artifact_and_checks_them_against_the_file_list()
	{
		var artifact = await PackAsync();

		var (output, error, exitCode) = await CliRunner.Run("inspect", "--artifact", artifact, "--no-color");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.Success), error);
			Assert.That(output, Does.Contain("logos: icon-packs/logos.macroDeckIconPack - Logos, 2 icon(s)"));
			Assert.That(output, Does.Not.Contain("logos.macroDeckIconPack - Logos, 2 icon(s) (not in files)"));
			Assert.That(error, Does.Contain("warning bundled-icon-pack-missing:").And.Contain("not in the artifact"));
			Assert.That(error, Does.Contain("warning bundled-icon-pack-not-in-files:").And.Contain("'gone'"));
		});
	}

	[Test]
	public async Task Validate_warns_about_a_declared_pack_that_does_not_exist_without_failing()
	{
		var (output, error, exitCode) = await CliRunner.Run("validate", "--directory", _directory, "--no-color");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.Success), output + error);
			Assert.That(output, Does.Contain("warning bundled-icon-pack-missing:").And.Contain("'gone'"));
			Assert.That(output, Does.Not.Contain("'logos'"));
		});
	}

	[Test]
	public async Task Validate_warns_when_a_packed_manifest_leaves_a_declared_pack_out_of_its_file_list()
	{
		var artifact = await PackAsync();

		var (output, error, _) = await CliRunner.Run("validate", "--artifact", artifact, "--no-color");

		Assert.Multiple(() =>
		{
			Assert.That(output, Does.Contain("warning bundled-icon-pack-not-in-files:").And.Contain("'gone'"), error);
			Assert.That(output, Does.Not.Contain("bundled-icon-pack-not-in-files: Bundled icon pack 'logos'"));
		});
	}

	private async Task<string> PackAsync()
	{
		var outputPath = Path.Combine(_output, "plugin.macroDeckPlugin");
		var pack = await PluginPacker.PackAsync(_directory,
			Path.Combine(_directory, PluginArtifactFiles.ManifestFileName),
			_ => outputPath,
			force: true);

		Assert.That(pack.Success, Is.True, pack.FailureMessage);
		return outputPath;
	}
}
