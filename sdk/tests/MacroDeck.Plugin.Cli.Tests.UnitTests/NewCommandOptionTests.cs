using System.Text.Json;
using MacroDeck.Plugin.Cli.Scaffolding;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>Drives <c>macrodeck-plugin new</c> end to end through <see cref="CliRunner" />, with a
/// <see cref="FakePluginScaffoldGenerator" /> standing in for the real <c>dotnet new</c> toolchain - see
/// issue #589's acceptance scenarios a-c, e and g.</summary>
[TestFixture]
public class NewCommandOptionTests
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

	private string[] CanonicalArgs(params string[] extra) =>
	[
		"new",
		"--name", "Spotify Controller",
		"--id", "com.example.spotify",
		"--publisher", "Example Publisher",
		"--platform", "win-x64",
		"--platform", "osx-arm64",
		"--platform", "linux-x64",
		"--output", _output,
		.. extra
	];

	private static JsonDocument ReadManifest(string output) =>
		JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "manifest.json")));

	private static JsonDocument ReadBuildConfig(string output) =>
		JsonDocument.Parse(File.ReadAllText(Path.Combine(output, PluginScaffoldDefaults.BuildConfigFileName)));

	// R0
	[Test]
	public async Task Canonical_invocation_produces_the_documented_manifest()
	{
		var (_, error, exitCode) = await CliRunner.Run(NewGenerator(), CanonicalArgs());

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.Success));
			Assert.That(error, Does.Not.Contain("error "));
		});

		using var manifest = ReadManifest(_output);
		var root = manifest.RootElement;

		Assert.Multiple(() =>
		{
			Assert.That(root.GetProperty("id").GetString(), Is.EqualTo("com.example.spotify"));
			Assert.That(root.GetProperty("name").GetString(), Is.EqualTo("Spotify Controller"));
			Assert.That(root.GetProperty("version").GetString(), Is.EqualTo("1.0.0"));
			Assert.That(root.GetProperty("manifestVersion").GetInt32(), Is.EqualTo(1));

			var entrypoints = root.GetProperty("entrypoints");
			var keys = entrypoints.EnumerateObject().Select(p => p.Name).ToList();
			Assert.That(keys, Is.EquivalentTo(new List<string> { "win-x64", "osx-arm64", "linux-x64" }));

			Assert.That(entrypoints.GetProperty("win-x64").GetProperty("executable").GetString(),
				Is.EqualTo("runtimes/win-x64/SpotifyController.exe"));
			Assert.That(entrypoints.GetProperty("osx-arm64").GetProperty("executable").GetString(),
				Is.EqualTo("runtimes/osx-arm64/SpotifyController"));
			Assert.That(entrypoints.GetProperty("linux-x64").GetProperty("executable").GetString(),
				Is.EqualTo("runtimes/linux-x64/SpotifyController"));

			foreach (var property in entrypoints.EnumerateObject())
			{
				Assert.That(property.Value.TryGetProperty("runtime", out _), Is.False);
			}
		});
	}

	// a1
	[Test]
	public async Task An_invalid_plugin_id_fails_before_anything_is_created()
	{
		var args = CanonicalArgs().Select(a => a == "com.example.spotify" ? "Com.Example.Spotify" : a).ToArray();
		var (_, error, exitCode) = await CliRunner.Run(NewGenerator(), args);

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
			Assert.That(error,
				Does.Contain("error invalid-plugin-id: Owner id must be reverse-domain, " +
					"lowercase and hyphen-separated, with at least two segments (e.g. 'com.example.my-plugin')."));
			Assert.That(Directory.Exists(_output), Is.False);
		});
	}

	// a2
	[Test]
	public async Task A_single_segment_id_is_rejected()
	{
		var args = CanonicalArgs().Select(a => a == "com.example.spotify" ? "spotify" : a).ToArray();
		var (_, error, exitCode) = await CliRunner.Run(NewGenerator(), args);

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
			Assert.That(error, Does.Contain("error invalid-plugin-id: "));
			Assert.That(Directory.Exists(_output), Is.False);
		});
	}

	// a3
	[Test]
	public async Task An_over_length_id_is_rejected()
	{
		var overLength = "com.example." + new string('a', 129);
		var args = CanonicalArgs().Select(a => a == "com.example.spotify" ? overLength : a).ToArray();
		var (_, error, exitCode) = await CliRunner.Run(NewGenerator(), args);

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
			Assert.That(error, Does.Contain("error invalid-plugin-id: Owner id must be at most 128 characters."));
		});
	}

	// a4
	[Test]
	public async Task A_non_http_repository_url_is_rejected()
	{
		var (_, error, exitCode) = await CliRunner.Run(NewGenerator(),
			CanonicalArgs("--repository", "git@github.com:example/spotify.git"));

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
			Assert.That(error, Does.Contain("error invalid-repository-url: "));
			Assert.That(Directory.Exists(_output), Is.False);
		});
	}

	[Test]
	public async Task An_ftp_homepage_url_is_rejected()
	{
		var (_, error, exitCode)
			= await CliRunner.Run(NewGenerator(), CanonicalArgs("--homepage", "ftp://example.com"));

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
			Assert.That(error, Does.Contain("error invalid-homepage-url: "));
			Assert.That(Directory.Exists(_output), Is.False);
		});
	}

	// a5
	[Test]
	public async Task An_unknown_platform_is_rejected_with_the_full_vocabulary()
	{
		var args = CanonicalArgs().Select(a => a == "win-x64" ? "win-x86" : a).ToArray();
		var (_, error, exitCode) = await CliRunner.Run(NewGenerator(), args);

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
			Assert.That(error,
				Does.Contain("error unknown-platform: 'win-x86' is not a supported target " +
					"platform. Supported: win-x64, win-arm64, osx-arm64, osx-x64, linux-x64, linux-arm64."));
		});
	}

	// a6
	[Test]
	public async Task A_non_empty_output_directory_fails_before_the_template_is_touched()
	{
		Directory.CreateDirectory(_output);
		await File.WriteAllTextAsync(Path.Combine(_output, "README.md"), "keep me");

		var generator = NewGenerator();
		var (_, error, exitCode) = await CliRunner.Run(generator, CanonicalArgs());

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
			Assert.That(error, Does.Contain("error output-exists: "));
			Assert.That(error, Does.Contain("already exists and is not empty."));
			Assert.That(File.ReadAllText(Path.Combine(_output, "README.md")), Is.EqualTo("keep me"));
			Assert.That(File.Exists(Path.Combine(_output, "manifest.json")), Is.False);
			Assert.That(generator.Calls, Is.Empty);
		});
	}

	// a7
	[Test]
	public async Task An_undeliverable_project_name_with_no_override_is_rejected()
	{
		var args = CanonicalArgs().Select(a => a == "Spotify Controller" ? "！！！" : a).ToArray();
		var (_, error, exitCode) = await CliRunner.Run(NewGenerator(), args);

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
			Assert.That(error, Does.Contain("error invalid-project-name: "));
			Assert.That(Directory.Exists(_output), Is.False);
		});
	}

	[Test]
	public async Task An_explicit_invalid_project_name_is_rejected()
	{
		var (_, error, exitCode) = await CliRunner.Run(NewGenerator(), CanonicalArgs("--project-name", "9Lives"));

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
			Assert.That(error, Does.Contain("error invalid-project-name: "));
		});
	}

	// b4
	[Test]
	public async Task A_version_option_is_unrecognized()
	{
		var (_, _, exitCode) = await CliRunner.Run(NewGenerator(), CanonicalArgs("--version", "2.3.4"));

		Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
	}

	[Test]
	public async Task Canonical_version_is_fixed_at_one_zero_zero()
	{
		await CliRunner.Run(NewGenerator(), CanonicalArgs());

		using var manifest = ReadManifest(_output);
		Assert.That(manifest.RootElement.GetProperty("version").GetString(), Is.EqualTo("1.0.0"));
	}

	// b5
	[Test]
	public async Task Csproj_and_entrypoints_use_the_explicit_project_name()
	{
		var generator = new FakePluginScaffoldGenerator
		{
			OnCreate = request =>
			{
				ScaffoldFixtures.WriteTemplateProject(request);
				File.WriteAllText(Path.Combine(request.Output, $"{request.ProjectName}.csproj"), string.Empty);
			}
		};

		await CliRunner.Run(generator, CanonicalArgs("--project-name", "SpotifyCtl"));

		Assert.Multiple(() =>
		{
			Assert.That(File.Exists(Path.Combine(_output, "SpotifyCtl.csproj")), Is.True);

			using var manifest = ReadManifest(_output);
			Assert.That(manifest.RootElement.GetProperty("entrypoints").GetProperty("win-x64")
					.GetProperty("executable").GetString(),
				Is.EqualTo("runtimes/win-x64/SpotifyCtl.exe"));
		});
	}

	// c1
	[Test]
	public async Task Only_selected_platforms_appear_in_both_generated_files()
	{
		var args = new[]
		{
			"new", "--name", "Spotify Controller", "--id", "com.example.spotify", "--publisher", "Example Publisher",
			"--platform", "osx-arm64", "--platform", "linux-arm64", "--output", _output
		};

		var (_, _, exitCode) = await CliRunner.Run(NewGenerator(), args);
		Assert.That(exitCode, Is.EqualTo(ExitCode.Success));

		using var manifest = ReadManifest(_output);
		var manifestKeys = manifest.RootElement.GetProperty("entrypoints").EnumerateObject().Select(p => p.Name)
			.ToList();
		Assert.That(manifestKeys, Is.EquivalentTo(new List<string> { "osx-arm64", "linux-arm64" }));

		using var buildConfig = ReadBuildConfig(_output);
		var buildKeys = buildConfig.RootElement.GetProperty("targets").EnumerateObject().Select(p => p.Name).ToList();
		Assert.That(buildKeys, Is.EquivalentTo(new List<string> { "osx-arm64", "linux-arm64" }));

		Assert.That(File.ReadAllText(Path.Combine(_output, "manifest.json")), Does.Not.Contain("win-x64"));
		Assert.That(File.ReadAllText(Path.Combine(_output, PluginScaffoldDefaults.BuildConfigFileName)),
			Does.Not.Contain("win-x64"));
	}

	// c5
	[Test]
	public async Task The_manifest_carries_no_build_instructions()
	{
		await CliRunner.Run(NewGenerator(), CanonicalArgs());

		using var manifest = ReadManifest(_output);
		var names = new HashSet<string>();
		CollectPropertyNames(manifest.RootElement, names);

		Assert.That(names.Intersect(["command", "script", "shell", "build", "publish", "targets"],
				StringComparer.OrdinalIgnoreCase),
			Is.Empty);
	}

	private static void CollectPropertyNames(JsonElement element, HashSet<string> names)
	{
		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				foreach (var property in element.EnumerateObject())
				{
					names.Add(property.Name);
					CollectPropertyNames(property.Value, names);
				}

				break;
			case JsonValueKind.Array:
				foreach (var item in element.EnumerateArray())
				{
					CollectPropertyNames(item, names);
				}

				break;
		}
	}

	// e1
	[Test]
	public async Task Zero_prompts_when_the_invocation_is_already_complete()
	{
		var (_, error, exitCode) = await CliRunner.Run(NewGenerator(), CanonicalArgs());

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.Success));
			Assert.That(error, Does.Not.Contain("error "));
		});
	}

	// e2
	[Test]
	public async Task Missing_required_values_are_named_together_without_a_terminal()
	{
		var (_, error, exitCode) = await CliRunner.Run(NewGenerator(),
			"new",
			"--name",
			"Spotify Controller",
			"--platform",
			"win-x64");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
			Assert.That(error, Does.Contain("error missing-required-option: "));
			Assert.That(error, Does.Contain("--id"));
			Assert.That(error, Does.Contain("--publisher"));
		});
	}

	[Test]
	public async Task Yes_takes_the_derived_id_while_non_interactive_insists_on_an_explicit_one()
	{
		var output = Path.Combine(Directory.CreateTempSubdirectory("macrodeck-cli-tests-").FullName, "Scaffold");

		var (_, yesError, yesExitCode) = await CliRunner.Run(NewGenerator(),
			"new",
			"--name",
			"Spotify Controller",
			"--publisher",
			"Example Publisher",
			"--platform",
			"win-x64",
			"--output",
			output,
			"--yes");

		var (_, strictError, strictExitCode) = await CliRunner.Run(NewGenerator(),
			"new",
			"--name",
			"Spotify Controller",
			"--publisher",
			"Example Publisher",
			"--platform",
			"win-x64",
			"--non-interactive");

		using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "manifest.json")));

		Assert.Multiple(() =>
		{
			Assert.That(yesExitCode, Is.EqualTo(ExitCode.Success));
			Assert.That(yesError, Does.Not.Contain("error "));
			Assert.That(document.RootElement.GetProperty("id").GetString(),
				Is.EqualTo(PluginIdDerivation.Derive("Spotify Controller")));

			Assert.That(strictExitCode, Is.EqualTo(ExitCode.UsageError));
			Assert.That(strictError, Does.Contain("error missing-required-option: "));
			Assert.That(strictError, Does.Contain("--id"));
		});
	}

	// e4
	[Test]
	public async Task Repeated_platform_flags_dedupe()
	{
		var args = new[]
		{
			"new", "--name", "Spotify Controller", "--id", "com.example.spotify", "--publisher", "Example Publisher",
			"--platform", "win-x64", "--platform", "win-x64", "--platform", "linux-x64", "--output", _output
		};

		var (_, _, exitCode) = await CliRunner.Run(NewGenerator(), args);
		Assert.That(exitCode, Is.EqualTo(ExitCode.Success));

		using var manifest = ReadManifest(_output);
		var keys = manifest.RootElement.GetProperty("entrypoints").EnumerateObject().Select(p => p.Name).ToList();
		Assert.That(keys, Is.EquivalentTo(new List<string> { "win-x64", "linux-x64" }));

		using var buildConfig = ReadBuildConfig(_output);
		Assert.That(buildConfig.RootElement.GetProperty("targets").EnumerateObject().Count(), Is.EqualTo(2));
	}

	// Narrowing the wizard's checkbox choices to PluginScaffoldDefaults.OfferedPlatforms (issue #756) must
	// never narrow --platform's own vocabulary: every one of the six KnownPlatforms stays a valid flag value.
	[Test]
	public async Task A_platform_not_offered_by_the_wizard_still_scaffolds_via_the_flag()
	{
		var args = new[]
		{
			"new", "--name", "Spotify Controller", "--id", "com.example.spotify", "--publisher", "Example Publisher",
			"--platform", "win-arm64", "--output", _output
		};

		var (_, _, exitCode) = await CliRunner.Run(NewGenerator(), args);
		Assert.That(exitCode, Is.EqualTo(ExitCode.Success));

		using var manifest = ReadManifest(_output);
		var keys = manifest.RootElement.GetProperty("entrypoints").EnumerateObject().Select(p => p.Name).ToList();
		Assert.That(keys, Is.EqualTo(new List<string> { "win-arm64" }));
	}

	// g2
	[Test]
	public async Task Quiet_verbosity_suppresses_narration_but_not_errors()
	{
		var (output, _, exitCode) = await CliRunner.Run(NewGenerator(), CanonicalArgs("--verbosity", "quiet"));

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.Success));
			Assert.That(output, Is.Empty);
			Assert.That(File.Exists(Path.Combine(_output, "manifest.json")), Is.True);
		});

		var args = CanonicalArgs("--verbosity", "quiet")
			.Select(a => a == "com.example.spotify" ? "Com.Example.Spotify" : a)
			.ToArray();
		var (_, error, _) = await CliRunner.Run(NewGenerator(), args);
		Assert.That(error, Does.Contain("error invalid-plugin-id: "));
	}

	// g3
	[Test]
	public async Task No_color_strips_ansi_and_the_default_adds_it()
	{
		var args = CanonicalArgs("--no-color").Select(a => a == "com.example.spotify" ? "Com.Example.Spotify" : a)
			.ToArray();
		var (_, noColorError, _) = await CliRunner.Run(NewGenerator(), args);

		Assert.Multiple(() =>
		{
			Assert.That(noColorError, Does.Contain("error invalid-plugin-id:"));
			Assert.That(noColorError, Does.Not.Contain("[31m"));
		});

		var colorArgs = CanonicalArgs().Select(a => a == "com.example.spotify" ? "Com.Example.Spotify" : a).ToArray();
		var (_, coloredError, _) = await CliRunner.Run(NewGenerator(), colorArgs);
		Assert.That(coloredError, Does.Contain("[31m"));
	}

	// g4
	[Test]
	public async Task Root_help_lists_new()
	{
		var (output, _, _) = await CliRunner.Run("--help");
		Assert.That(output, Does.Contain("new"));
	}

	// f4
	[Test]
	public async Task Template_install_failure_exits_input_unreadable_and_creates_nothing()
	{
		var generator = new FakePluginScaffoldGenerator
		{
			ProbeResult = PluginTemplateStatus.NotInstalled,
			InstallResult = new PluginTemplateOperationResult(false, "error NU1101: Unable to find package")
		};

		var (_, error, exitCode) = await CliRunner.Run(generator, CanonicalArgs());

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.InputUnreadable));
			Assert.That(error, Does.Contain("error template-install-failed: "));
			Assert.That(error, Does.Contain("error NU1101: Unable to find package"));
			Assert.That(Directory.Exists(_output), Is.False);
		});
	}

	// f5
	[Test]
	public async Task Dotnet_not_launchable_exits_input_unreadable()
	{
		var generator = new FakePluginScaffoldGenerator
		{
			ThrowOn = new PluginScaffoldGeneratorException(PluginScaffoldFailureReason.DotnetNotFound,
				"Could not launch 'dotnet'. Install the .NET SDK and ensure it is on PATH.")
		};

		var (_, error, exitCode) = await CliRunner.Run(generator, CanonicalArgs());

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.InputUnreadable));
			Assert.That(error, Does.Contain("error dotnet-not-found: "));
			Assert.That(error, Does.Contain("dotnet"));
			Assert.That(Directory.Exists(_output), Is.False);
		});
	}

	// f6
	[Test]
	public async Task Create_failure_exits_subject_invalid_and_removes_the_partial_tree()
	{
		var generator = new FakePluginScaffoldGenerator
		{
			CreateResult = new PluginTemplateOperationResult(false, "template error"),
			OnCreate = request => File.WriteAllText(Path.Combine(request.Output, "partial.txt"), "partial")
		};

		var (_, error, exitCode) = await CliRunner.Run(generator, CanonicalArgs());

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.SubjectInvalid));
			Assert.That(error, Does.Contain("error template-create-failed: "));
			Assert.That(Directory.Exists(_output), Is.False);
		});
	}
}
