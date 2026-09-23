using System.Text.Json;
using MacroDeck.Plugin.Packaging.Artifacts;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

[TestFixture]
internal sealed class InspectAiDeclarationTests
{
	private string _directory = null!;

	[TearDown]
	public void TearDown()
	{
		if (Directory.Exists(_directory))
		{
			Directory.Delete(_directory, recursive: true);
		}
	}

	private void WriteManifest(string extra)
	{
		_directory = ManifestFixtures.WriteValidManifestDirectory();
		var manifestPath = Path.Combine(_directory, PluginArtifactFiles.ManifestFileName);
		var json = File.ReadAllText(manifestPath).TrimEnd();
		File.WriteAllText(manifestPath, json[..^1].TrimEnd() + extra + "\n}");
	}

	[Test]
	public async Task Inspect_says_when_a_plugin_declares_nothing_about_ai()
	{
		WriteManifest(string.Empty);

		var (output, _, exitCode) = await CliRunner.Run("inspect", "--directory", _directory, "--no-color");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.Success));
			Assert.That(output, Does.Contain("AI: (not declared)"));
		});
	}

	[Test]
	public async Task Inspect_lists_declared_ai_use_in_text_and_json()
	{
		WriteManifest(""", "ai": { "interaction": true, "services": ["OpenAI"] }""");

		var (text, _, _) = await CliRunner.Run("inspect", "--directory", _directory, "--no-color");
		var (json, _, _) =
			await CliRunner.Run("inspect", "--directory", _directory, "--output", "json", "--no-color");

		using var document = JsonDocument.Parse(json);
		var ai = document.RootElement.GetProperty("ai");
		Assert.Multiple(() =>
		{
			Assert.That(text, Does.Contain("AI: interaction, services: OpenAI"));
			Assert.That(ai.GetProperty("interaction").GetBoolean(), Is.True);
			Assert.That(ai.GetProperty("services")[0].GetString(), Is.EqualTo("OpenAI"));
		});
	}

	[Test]
	public async Task Inspect_never_reports_a_service_only_declaration_as_no_ai()
	{
		WriteManifest(""", "ai": { "services": ["OpenAI"] }""");

		var (output, _, _) = await CliRunner.Run("inspect", "--directory", _directory, "--no-color");

		Assert.That(output, Does.Contain("AI: services: OpenAI"));
	}
}
