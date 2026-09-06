using System.Text.Json;
using MacroDeck.Plugin.Cli.Scaffolding;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>Drives <c>macrodeck-plugin new</c>'s interactive wizard through <see cref="CliRunner.RunInteractive" />
/// - see issue #589's acceptance scenario d.</summary>
[TestFixture]
public class NewCommandWizardTests
{
	private string _output = null!;

	[SetUp]
	public void SetUp()
	{
		_output = Path.Combine(Directory.CreateTempSubdirectory("macrodeck-cli-tests-").FullName, "SpotifyController");
	}

	private static FakePluginScaffoldGenerator NewGenerator() => new()
	{
		OnCreate = ScaffoldFixtures.WriteTemplateProject
	};

	// d1
	[Test]
	public async Task Wizard_output_matches_the_canonical_non_interactive_run()
	{
		var script = string.Join('\n',
			"Spotify Controller",
			"com.example.spotify",
			"Example Publisher",
			"",
			"",
			"",
			"",
			"1,2,3",
			_output,
			"y");

		var wizardGenerator = NewGenerator();
		var (_, _, wizardExitCode) = await CliRunner.RunInteractive(script, wizardGenerator, "new");
		Assert.That(wizardExitCode, Is.EqualTo(ExitCode.Success));

		var flagOutput = Path.Combine(Directory.CreateTempSubdirectory("macrodeck-cli-tests-").FullName,
			"SpotifyController");
		var flagArgs = new[]
		{
			"new", "--name", "Spotify Controller", "--id", "com.example.spotify", "--publisher", "Example Publisher",
			"--platform", "win-x64", "--platform", "osx-arm64", "--platform", "linux-x64", "--output", flagOutput
		};
		var flagGenerator = NewGenerator();
		var (_, _, flagExitCode) = await CliRunner.Run(flagGenerator, flagArgs);
		Assert.That(flagExitCode, Is.EqualTo(ExitCode.Success));

		var wizardManifest = NormalizeJson(File.ReadAllText(Path.Combine(_output, "manifest.json")));
		var flagManifest = NormalizeJson(File.ReadAllText(Path.Combine(flagOutput, "manifest.json")));
		Assert.That(wizardManifest, Is.EqualTo(flagManifest));

		var wizardBuildConfig = NormalizeJson(
			File.ReadAllText(Path.Combine(_output, PluginScaffoldDefaults.BuildConfigFileName)));
		var flagBuildConfig = NormalizeJson(
			File.ReadAllText(Path.Combine(flagOutput, PluginScaffoldDefaults.BuildConfigFileName)));
		Assert.That(wizardBuildConfig, Is.EqualTo(flagBuildConfig));
	}

	private static string NormalizeJson(string json)
	{
		using var document = JsonDocument.Parse(json);
		return JsonSerializer.Serialize(document.RootElement);
	}

	// d2
	[Test]
	public async Task Id_prompt_default_is_derived_from_the_plugin_name()
	{
		var script = string.Join('\n', "Spotify Controller", "", "Example Publisher", "", "", "", "", "", _output, "n");
		var (output, _, exitCode) = await CliRunner.RunInteractive(script, "new");

		Assert.That(exitCode, Is.EqualTo(ExitCode.Cancelled));
		Assert.That(output, Does.Contain("spotify-controller"));
	}

	[Test]
	public async Task Accepting_the_derived_id_default_yields_it_verbatim()
	{
		var script = string.Join('\n', "Spotify Controller", "", "Example Publisher", "", "", "", "", "", _output, "y");
		var generator = NewGenerator();
		var (_, _, exitCode) = await CliRunner.RunInteractive(script, generator, "new");

		Assert.That(exitCode, Is.EqualTo(ExitCode.Success));

		using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(_output, "manifest.json")));
		Assert.That(manifest.RootElement.GetProperty("id").GetString(), Is.EqualTo("com.example.spotify-controller"));
	}

	// d3
	[Test]
	public async Task An_invalid_id_answer_re_prompts_instead_of_aborting()
	{
		var script = string.Join('\n',
			"Spotify Controller",
			"NOT VALID",
			"com.example.spotify",
			"Example Publisher",
			"",
			"",
			"",
			"",
			"",
			_output,
			"y");

		var generator = NewGenerator();
		var (_, error, exitCode) = await CliRunner.RunInteractive(script, generator, "new");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.Success));
			Assert.That(error, Does.Contain("invalid-plugin-id"));
		});

		using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(_output, "manifest.json")));
		Assert.That(manifest.RootElement.GetProperty("id").GetString(), Is.EqualTo("com.example.spotify"));
	}

	// d4
	[Test]
	public async Task An_empty_name_re_prompts_and_an_empty_repository_proceeds()
	{
		var script = string.Join('\n',
			"",
			"Spotify Controller",
			"com.example.spotify",
			"Example Publisher",
			"",
			"",
			"",
			"",
			"",
			_output,
			"y");

		var generator = NewGenerator();
		var (_, _, exitCode) = await CliRunner.RunInteractive(script, generator, "new");
		Assert.That(exitCode, Is.EqualTo(ExitCode.Success));

		using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(_output, "manifest.json")));
		Assert.Multiple(() =>
		{
			Assert.That(manifest.RootElement.GetProperty("name").GetString(), Is.EqualTo("Spotify Controller"));
			Assert.That(manifest.RootElement.TryGetProperty("repository", out _), Is.False);
		});
	}

	// d5
	[Test]
	public async Task Fallback_platform_prompt_lists_only_the_offered_rids_and_the_default()
	{
		var script = string.Join('\n', "Spotify Controller", "", "Example Publisher", "", "", "", "", "", _output, "n");
		var (output, _, _) = await CliRunner.RunInteractive(script, "new");

		Assert.Multiple(() =>
		{
			foreach (var rid in PluginScaffoldDefaults.OfferedPlatforms)
			{
				Assert.That(output, Does.Contain(rid));
			}

			for (var i = 1; i <= 3; i++)
			{
				Assert.That(output, Does.Contain($"{i})"));
			}

			Assert.That(output, Does.Contain("win-x64, osx-arm64, linux-x64"));

			Assert.That(output, Does.Not.Contain("win-arm64"));
			Assert.That(output, Does.Not.Contain("osx-x64"));
			Assert.That(output, Does.Not.Contain("linux-arm64"));
			Assert.That(output, Does.Not.Contain("  4)"));
		});
	}

	[Test]
	public async Task Selecting_2_and_3_yields_exactly_those_two_platforms()
	{
		var script = string.Join('\n',
			"Spotify Controller",
			"",
			"Example Publisher",
			"",
			"",
			"",
			"",
			"2,3",
			_output,
			"y");
		var generator = NewGenerator();
		var (_, _, exitCode) = await CliRunner.RunInteractive(script, generator, "new");
		Assert.That(exitCode, Is.EqualTo(ExitCode.Success));

		using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(_output, "manifest.json")));
		var keys = manifest.RootElement.GetProperty("entrypoints").EnumerateObject().Select(p => p.Name).ToList();
		Assert.That(keys, Is.EquivalentTo(new List<string> { "osx-arm64", "linux-x64" }));
	}

	[Test]
	public async Task An_out_of_range_fallback_index_re_prompts_instead_of_aborting()
	{
		var script = string.Join('\n',
			"Spotify Controller",
			"",
			"Example Publisher",
			"",
			"",
			"",
			"",
			"4",
			"2",
			_output,
			"y");
		var generator = NewGenerator();
		var (_, error, exitCode) = await CliRunner.RunInteractive(script, generator, "new");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.Success));
			Assert.That(error, Does.Contain("unknown-platform"));
		});

		using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(_output, "manifest.json")));
		var keys = manifest.RootElement.GetProperty("entrypoints").EnumerateObject().Select(p => p.Name).ToList();
		Assert.That(keys, Is.EquivalentTo(new List<string> { "osx-arm64" }));
	}

	[Test]
	public async Task An_empty_platform_answer_yields_exactly_the_three_defaults()
	{
		var script = string.Join('\n', "Spotify Controller", "", "Example Publisher", "", "", "", "", "", _output, "y");
		var generator = NewGenerator();
		var (_, _, exitCode) = await CliRunner.RunInteractive(script, generator, "new");
		Assert.That(exitCode, Is.EqualTo(ExitCode.Success));

		using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(_output, "manifest.json")));
		var keys = manifest.RootElement.GetProperty("entrypoints").EnumerateObject().Select(p => p.Name).ToList();
		Assert.That(keys, Is.EquivalentTo(new List<string> { "win-x64", "osx-arm64", "linux-x64" }));
	}

	// d6
	[Test]
	public async Task Declining_the_confirmation_writes_nothing()
	{
		var script = string.Join('\n', "Spotify Controller", "", "Example Publisher", "", "", "", "", "", _output, "n");
		var generator = NewGenerator();
		var (_, error, exitCode) = await CliRunner.RunInteractive(script, generator, "new");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.Cancelled));
			Assert.That(error, Does.Not.Contain("error "));
			Assert.That(Directory.Exists(_output), Is.False);
			Assert.That(generator.Calls, Is.Empty);
		});
	}

	// e3
	[Test]
	public async Task Supplied_flags_are_never_prompted_for_and_the_wizard_fills_only_the_gaps()
	{
		var script = string.Join('\n', "Example Publisher", "", "", "", "", "", "y");

		var (output, _, exitCode) = await CliRunner.RunInteractive(script,
			NewGenerator(),
			"new",
			"--name",
			"Spotify Controller",
			"--id",
			"com.example.spotify",
			"--platform",
			"osx-arm64",
			"--output",
			_output);

		Assert.That(exitCode, Is.EqualTo(ExitCode.Success));

		using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(_output, "manifest.json")));
		var root = document.RootElement;
		var entrypointKeys = root.GetProperty("entrypoints").EnumerateObject().Select(p => p.Name).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(root.GetProperty("publisher").GetProperty("name").GetString(), Is.EqualTo("Example Publisher"));
			Assert.That(root.GetProperty("name").GetString(), Is.EqualTo("Spotify Controller"));
			Assert.That(root.GetProperty("id").GetString(), Is.EqualTo("com.example.spotify"));
			Assert.That(entrypointKeys, Is.EquivalentTo(new List<string> { "osx-arm64" }));

			Assert.That(output, Does.Not.Contain("Plugin name"));
			Assert.That(output, Does.Not.Contain("Plugin id"));
			Assert.That(output, Does.Not.Contain("Target platforms"));
			Assert.That(output, Does.Contain("Publisher name"));
		});
	}

	[Test]
	public async Task The_repository_url_is_the_offered_default_for_the_homepage()
	{
		var script = string.Join('\n',
			"Spotify Controller",
			"com.example.spotify",
			"Example Publisher",
			"",
			"https://github.com/example/spotify",
			"",
			"",
			"",
			_output,
			"y");

		var (output, _, exitCode) = await CliRunner.RunInteractive(script, NewGenerator(), "new");
		Assert.That(exitCode, Is.EqualTo(ExitCode.Success));

		using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(_output, "manifest.json")));

		Assert.Multiple(() =>
		{
			Assert.That(output, Does.Contain("Homepage URL [https://github.com/example/spotify]"));
			Assert.That(document.RootElement.GetProperty("homepage").GetString(),
				Is.EqualTo("https://github.com/example/spotify"));
		});
	}

	[Test]
	public async Task A_repository_flag_alone_does_not_fill_the_homepage()
	{
		var args = new[]
		{
			"new", "--name", "Spotify Controller", "--id", "com.example.spotify", "--publisher", "Example Publisher",
			"--repository", "https://github.com/example/spotify", "--platform", "win-x64", "--output", _output
		};

		var (_, _, exitCode) = await CliRunner.Run(NewGenerator(), args);
		Assert.That(exitCode, Is.EqualTo(ExitCode.Success));

		using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(_output, "manifest.json")));

		Assert.Multiple(() =>
		{
			Assert.That(document.RootElement.GetProperty("repository").GetString(),
				Is.EqualTo("https://github.com/example/spotify"));
			Assert.That(document.RootElement.TryGetProperty("homepage", out _), Is.False);
		});
	}

	[Test]
	public async Task No_repository_leaves_the_homepage_absent()
	{
		var args = new[]
		{
			"new", "--name", "Spotify Controller", "--id", "com.example.spotify", "--publisher", "Example Publisher",
			"--platform", "win-x64", "--output", _output
		};

		var (_, _, exitCode) = await CliRunner.Run(NewGenerator(), args);
		Assert.That(exitCode, Is.EqualTo(ExitCode.Success));

		using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(_output, "manifest.json")));

		Assert.Multiple(() =>
		{
			Assert.That(document.RootElement.TryGetProperty("homepage", out _), Is.False);
			Assert.That(document.RootElement.TryGetProperty("repository", out _), Is.False);
		});
	}

	// d7
	[Test]
	public async Task Cancelling_the_token_while_awaiting_input_leaves_nothing_behind()
	{
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();

		var lines = new Queue<string>(["Spotify Controller"]);
		var prompt = new CliPromptReader(isInteractive: true, readLine: () => lines.Count > 0 ? lines.Dequeue() : null);

		var output = new StringWriter();
		var error = new StringWriter();
		var exitCode = await CliEntryPoint.RunAsync(["new"], output, error, prompt, ct: cancellation.Token);

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.Cancelled));
			Assert.That(Directory.Exists(_output), Is.False);
		});
	}
}
