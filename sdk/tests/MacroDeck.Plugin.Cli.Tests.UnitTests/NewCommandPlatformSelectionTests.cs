using System.Text.Json;
using MacroDeck.Plugin.Cli.Scaffolding;
using Spectre.Console.Testing;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>Drives <c>macrodeck-plugin new</c>'s interactive checkbox platform prompt through
/// <see cref="CliRunner.RunWithCheckboxPrompt" /> - see issue #756.</summary>
[TestFixture]
public class NewCommandPlatformSelectionTests
{
	private static FakePluginScaffoldGenerator NewGenerator() => new()
	{
		OnCreate = ScaffoldFixtures.WriteTemplateProject
	};

	private static string NewOutput() =>
		Path.Combine(Directory.CreateTempSubdirectory("macrodeck-cli-tests-").FullName, "SpotifyController");

	/// <summary>Every non-platform, non-output value supplied as a flag, so the wizard's line-based prompts
	/// never fire and the checkbox prompt is the first thing standing between the flags and the output
	/// directory prompt.</summary>
	private static string[] ArgsUpToPlatforms(params string[] extra) =>
	[
		"new",
		"--name", "Spotify Controller",
		"--id", "com.example.spotify",
		"--publisher", "Example Publisher",
		"--description", "A test plugin.",
		"--repository", "https://github.com/example/spotify",
		"--homepage", "https://github.com/example/spotify",
		"--license", "MIT",
		.. extra
	];

	private static JsonDocument ReadManifest(string output) =>
		JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "manifest.json")));

	private static JsonDocument ReadBuildConfig(string output) =>
		JsonDocument.Parse(File.ReadAllText(Path.Combine(output, PluginScaffoldDefaults.BuildConfigFileName)));

	private static List<string> EntrypointKeys(JsonDocument manifest) =>
		manifest.RootElement.GetProperty("entrypoints").EnumerateObject().Select(p => p.Name).ToList();

	private static List<string> TargetKeys(JsonDocument buildConfig) =>
		buildConfig.RootElement.GetProperty("targets").EnumerateObject().Select(p => p.Name).ToList();

	[Test]
	public async Task Offers_only_the_three_supported_platforms()
	{
		using var ansi = new TestConsole().Interactive().Width(80);
		ansi.Input.PushKey(ConsoleKey.Escape);

		var (output, _, exitCode) =
			await CliRunner.RunWithCheckboxPrompt(ansi, "", NewGenerator(), ArgsUpToPlatforms());

		Assert.That(exitCode, Is.EqualTo(ExitCode.Cancelled));

		var combined = ansi.Output + output;
		Assert.Multiple(() =>
		{
			Assert.That(combined, Does.Contain("win-x64"));
			Assert.That(combined, Does.Contain("osx-arm64"));
			Assert.That(combined, Does.Contain("linux-x64"));
			Assert.That(combined, Does.Not.Contain("win-arm64"));
			Assert.That(combined, Does.Not.Contain("osx-x64"));
			Assert.That(combined, Does.Not.Contain("linux-arm64"));
		});
	}

	[Test]
	public async Task Confirming_without_toggling_yields_the_three_defaults()
	{
		var output = NewOutput();
		using var ansi = new TestConsole().Interactive().Width(80);
		ansi.Input.PushKey(ConsoleKey.Enter);

		var stdin = string.Join('\n', output, "y");
		var (_, _, exitCode) =
			await CliRunner.RunWithCheckboxPrompt(ansi, stdin, NewGenerator(), ArgsUpToPlatforms());

		Assert.That(exitCode, Is.EqualTo(ExitCode.Success));

		using var manifest = ReadManifest(output);
		using var buildConfig = ReadBuildConfig(output);
		var expected = new List<string> { "win-x64", "osx-arm64", "linux-x64" };
		Assert.That(EntrypointKeys(manifest), Is.EqualTo(expected));
		Assert.That(TargetKeys(buildConfig), Is.EqualTo(expected));
	}

	[Test]
	public async Task Toggling_an_entry_off_is_honoured()
	{
		var output = NewOutput();
		using var ansi = new TestConsole().Interactive().Width(80);
		ansi.Input.PushKey(ConsoleKey.DownArrow);
		ansi.Input.PushKey(ConsoleKey.Spacebar);
		ansi.Input.PushKey(ConsoleKey.Enter);

		var stdin = string.Join('\n', output, "y");
		var (stdout, _, exitCode) =
			await CliRunner.RunWithCheckboxPrompt(ansi, stdin, NewGenerator(), ArgsUpToPlatforms());

		Assert.That(exitCode, Is.EqualTo(ExitCode.Success));

		using var manifest = ReadManifest(output);
		using var buildConfig = ReadBuildConfig(output);
		var expected = new List<string> { "win-x64", "linux-x64" };

		Assert.Multiple(() =>
		{
			Assert.That(EntrypointKeys(manifest), Is.EqualTo(expected));
			Assert.That(TargetKeys(buildConfig), Is.EqualTo(expected));
			Assert.That(File.ReadAllText(Path.Combine(output, "manifest.json")), Does.Not.Contain("osx-arm64"));
			Assert.That(File.ReadAllText(Path.Combine(output, PluginScaffoldDefaults.BuildConfigFileName)),
				Does.Not.Contain("osx-arm64"));
			Assert.That(stdout, Does.Contain("win-x64, linux-x64"));
			Assert.That(stdout, Does.Not.Contain("osx-arm64"));
		});
	}

	[Test]
	public async Task Escape_cancels_the_whole_command()
	{
		var output = NewOutput();
		using var ansi = new TestConsole().Interactive().Width(80);
		ansi.Input.PushKey(ConsoleKey.Escape);

		var generator = NewGenerator();
		var (_, error, exitCode) =
			await CliRunner.RunWithCheckboxPrompt(ansi, "", generator, ArgsUpToPlatforms());

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.Cancelled));
			Assert.That(Directory.Exists(output), Is.False);
			Assert.That(generator.Calls, Is.Empty);
			Assert.That(error, Does.Not.Contain("error "));
		});
	}

	[Test]
	public async Task No_color_keeps_the_checkbox_and_asks_for_a_colorless_one()
	{
		var output = NewOutput();
		using var ansi = new TestConsole().Interactive().Width(80);
		ansi.Input.PushKey(ConsoleKey.DownArrow);
		ansi.Input.PushKey(ConsoleKey.Spacebar);
		ansi.Input.PushKey(ConsoleKey.Enter);

		var noColorRequested = false;
		var (stdout, _, exitCode) = await CliRunner.RunWithCheckboxPrompt(ansi,
			string.Join('\n', output, "y"),
			NewGenerator(),
			noColor => noColorRequested = noColor,
			ArgsUpToPlatforms("--no-color"));

		Assert.That(exitCode, Is.EqualTo(ExitCode.Success));

		using var manifest = ReadManifest(output);

		Assert.Multiple(() =>
		{
			Assert.That(noColorRequested, Is.True);
			Assert.That(EntrypointKeys(manifest), Is.EqualTo(new List<string> { "win-x64", "linux-x64" }));
			Assert.That(stdout, Does.Not.Contain("comma-separated numbers"));
		});
	}

	// The wizard reads an empty selection as "the user pressed Esc", so confirming with nothing ticked must
	// not be mistakable for cancelling: the prompt refuses it and keeps asking instead.
	[Test]
	public async Task Confirming_with_nothing_selected_is_not_a_cancellation()
	{
		var output = NewOutput();
		using var ansi = new TestConsole().Interactive().Width(80);
		ansi.Input.PushKey(ConsoleKey.Spacebar);
		ansi.Input.PushKey(ConsoleKey.DownArrow);
		ansi.Input.PushKey(ConsoleKey.Spacebar);
		ansi.Input.PushKey(ConsoleKey.DownArrow);
		ansi.Input.PushKey(ConsoleKey.Spacebar);
		ansi.Input.PushKey(ConsoleKey.Enter);
		ansi.Input.PushKey(ConsoleKey.Spacebar);
		ansi.Input.PushKey(ConsoleKey.Enter);

		var stdin = string.Join('\n', output, "y");
		var (_, _, exitCode) =
			await CliRunner.RunWithCheckboxPrompt(ansi, stdin, NewGenerator(), ArgsUpToPlatforms());

		Assert.That(exitCode, Is.EqualTo(ExitCode.Success));

		using var manifest = ReadManifest(output);
		Assert.That(EntrypointKeys(manifest), Is.EqualTo(new List<string> { "linux-x64" }));
	}
}
