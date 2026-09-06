using MacroDeck.Plugin.Cli.Manifests;
using MacroDeck.Plugin.Cli.Scaffolding;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>Exercises <see cref="PluginScaffolder" /> against <see cref="FakePluginScaffoldGenerator" /> -
/// see issue #589's acceptance scenario f. No test here may invoke the real <c>dotnet new</c>.</summary>
[TestFixture]
public class PluginScaffolderTests
{
	private string _output = null!;

	[SetUp]
	public void SetUp()
	{
		_output = Path.Combine(Directory.CreateTempSubdirectory("macrodeck-cli-tests-").FullName, "SpotifyController");
	}

	private PluginScaffoldRequest CanonicalRequest() => new()
	{
		Name = "Spotify Controller",
		Id = "com.example.spotify",
		Publisher = "Example Publisher",
		Description = "A Macro Deck plugin.",
		License = "MIT",
		ProjectName = "SpotifyController",
		Output = _output,
		Platforms = ["win-x64", "osx-arm64", "linux-x64"]
	};

	[Test]
	public async Task Happy_path_rewrites_the_manifest_and_writes_a_sibling_build_config()
	{
		var generator = new FakePluginScaffoldGenerator { OnCreate = ScaffoldFixtures.WriteTemplateProject };

		var result = await PluginScaffolder.ScaffoldAsync(CanonicalRequest(), generator);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.ManifestPath, Is.EqualTo(Path.Combine(_output, "manifest.json")));
			Assert.That(result.BuildConfigPath,
				Is.EqualTo(Path.Combine(_output, PluginScaffoldDefaults.BuildConfigFileName)));
			Assert.That(File.Exists(result.ManifestPath!), Is.True);
			Assert.That(File.Exists(result.BuildConfigPath!), Is.True);
		});
	}

	[Test]
	public async Task No_manifest_generated_fails_cleanly()
	{
		var generator = new FakePluginScaffoldGenerator
			{ OnCreate = request => Directory.CreateDirectory(request.Output) };

		var result = await PluginScaffolder.ScaffoldAsync(CanonicalRequest(), generator);

		Assert.That(result.Success, Is.False);
		Assert.That(result.FailureReason, Is.EqualTo(PluginScaffoldFailureReason.ManifestNotGenerated));
	}

	[Test]
	public async Task Two_manifests_fails_cleanly()
	{
		var generator = new FakePluginScaffoldGenerator
		{
			OnCreate = request =>
			{
				ScaffoldFixtures.WriteTemplateProject(request);
				Directory.CreateDirectory(Path.Combine(request.Output, "nested"));
				File.WriteAllText(Path.Combine(request.Output, "nested", "manifest.json"),
					ScaffoldFixtures.TemplateManifestJson(request));
			}
		};

		var result = await PluginScaffolder.ScaffoldAsync(CanonicalRequest(), generator);

		Assert.That(result.Success, Is.False);
		Assert.That(result.FailureReason, Is.EqualTo(PluginScaffoldFailureReason.ManifestNotGenerated));
	}

	// f1
	[Test]
	public async Task Missing_template_installs_then_creates()
	{
		var generator = new FakePluginScaffoldGenerator
		{
			ProbeResult = PluginTemplateStatus.NotInstalled,
			OnCreate = ScaffoldFixtures.WriteTemplateProject
		};

		var result = await PluginScaffolder.ScaffoldAsync(CanonicalRequest(), generator);

		Assert.That(result.Success, Is.True);
		Assert.That(generator.Calls, Is.EqualTo(new List<string> { "ProbeAsync", "InstallAsync", "CreateAsync" }));
	}

	// f2
	[Test]
	public async Task Already_installed_never_installs_or_updates()
	{
		var generator = new FakePluginScaffoldGenerator
		{
			ProbeResult = PluginTemplateStatus.UpToDate,
			OnCreate = ScaffoldFixtures.WriteTemplateProject
		};

		var result = await PluginScaffolder.ScaffoldAsync(CanonicalRequest(), generator);

		Assert.That(result.Success, Is.True);
		Assert.That(generator.Calls, Does.Not.Contain("InstallAsync"));
		Assert.That(generator.Calls, Does.Not.Contain("UpdateAsync"));
	}

	// f3
	[Test]
	public async Task Outdated_template_updates_before_create()
	{
		var generator = new FakePluginScaffoldGenerator
		{
			ProbeResult = PluginTemplateStatus.Outdated,
			OnCreate = ScaffoldFixtures.WriteTemplateProject
		};

		var result = await PluginScaffolder.ScaffoldAsync(CanonicalRequest(), generator);

		Assert.That(result.Success, Is.True);
		Assert.That(generator.Calls, Is.EqualTo(new List<string> { "ProbeAsync", "UpdateAsync", "CreateAsync" }));
	}

	// f4
	[Test]
	public async Task Install_failure_leaves_nothing_created()
	{
		var generator = new FakePluginScaffoldGenerator
		{
			ProbeResult = PluginTemplateStatus.NotInstalled,
			InstallResult = new PluginTemplateOperationResult(false, "error NU1101: Unable to find package")
		};

		var result = await PluginScaffolder.ScaffoldAsync(CanonicalRequest(), generator);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.FailureReason, Is.EqualTo(PluginScaffoldFailureReason.TemplateInstallFailed));
			Assert.That(result.FailureDetail, Is.EqualTo("error NU1101: Unable to find package"));
			Assert.That(Directory.Exists(_output), Is.False);
		});
	}

	// f5
	[Test]
	public async Task Dotnet_not_launchable_is_reported_and_nothing_is_created()
	{
		var generator = new FakePluginScaffoldGenerator
		{
			ThrowOn = new PluginScaffoldGeneratorException(PluginScaffoldFailureReason.DotnetNotFound,
				"Could not launch 'dotnet'. Install the .NET SDK and ensure it is on PATH.")
		};

		var result = await PluginScaffolder.ScaffoldAsync(CanonicalRequest(), generator);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.FailureReason, Is.EqualTo(PluginScaffoldFailureReason.DotnetNotFound));
			Assert.That(Directory.Exists(_output), Is.False);
		});
	}

	// f6
	[Test]
	public async Task Create_failure_removes_the_directory_this_command_created()
	{
		var generator = new FakePluginScaffoldGenerator
		{
			CreateResult = new PluginTemplateOperationResult(false, "template error"),
			OnCreate = request => File.WriteAllText(Path.Combine(request.Output, "partial.txt"), "partial")
		};

		var result = await PluginScaffolder.ScaffoldAsync(CanonicalRequest(), generator);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.FailureReason, Is.EqualTo(PluginScaffoldFailureReason.TemplateCreateFailed));
			Assert.That(Directory.Exists(_output), Is.False);
		});
	}

	[Test]
	public async Task Create_failure_against_a_preexisting_empty_directory_leaves_it_empty()
	{
		Directory.CreateDirectory(_output);

		var generator = new FakePluginScaffoldGenerator
		{
			CreateResult = new PluginTemplateOperationResult(false, "template error")
		};

		var result = await PluginScaffolder.ScaffoldAsync(CanonicalRequest(), generator);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(Directory.Exists(_output), Is.True);
			Assert.That(Directory.EnumerateFileSystemEntries(_output), Is.Empty);
		});
	}

	[Test]
	public async Task New_scaffolds_a_macro_deck_compatibility_range_and_names_what_is_still_missing()
	{
		var generator = new FakePluginScaffoldGenerator { OnCreate = ScaffoldFixtures.WriteTemplateProject };

		// Foreign runtime identifiers, not CanonicalRequest's literal win-x64/osx-arm64/linux-x64: the fake
		// generator never builds a real binary, and validate must not existence-check whichever of those
		// happens to be this test machine's own current RID before this test's own new checks even run.
		var request = CanonicalRequest() with { Platforms = ManifestFixtures.PickForeignRids(3) };

		// No --repository: the request leaves Repository null, so it stays a genuine gap.
		var result = await PluginScaffolder.ScaffoldAsync(request, generator);
		Assert.That(result.Success, Is.True, result.FailureMessage);

		var manifestJson = await File.ReadAllTextAsync(result.ManifestPath!);
		using var document = System.Text.Json.JsonDocument.Parse(manifestJson);
		var compatibility = document.RootElement.GetProperty("compatibility");

		Assert.Multiple(() =>
		{
			Assert.That(compatibility.GetProperty("macroDeck").GetString(), Is.EqualTo(">=3.0.0"));
			Assert.That(compatibility.TryGetProperty("protocol", out _), Is.False);
		});

		// A real 'dotnet new' always writes a project file beside the manifest; FakePluginScaffoldGenerator's
		// stand-in template does not, so one is added here to keep validate's own "is this an unbuilt source
		// tree" judgement accurate - otherwise the platforms this scaffold declared but never built would
		// also report as missing payload, which is not what this test is about.
		await File.WriteAllTextAsync(Path.Combine(_output, "SpotifyController.csproj"),
			"<Project Sdk=\"Microsoft.NET.Sdk\" />");

		var validation =
			await ManifestValidator.ValidateManifestFileAsync(result.ManifestPath!,
				PluginManifestValidationLevel.Publication);

		var reportedPointers = validation.Problems.Select(problem => problem.Pointer).ToList();

		Assert.Multiple(() =>
		{
			// Genuinely absent: no --icon, no --repository.
			Assert.That(reportedPointers, Does.Contain("/icon"));
			Assert.That(reportedPointers, Does.Contain("/repository"));

			// Never named: 'new' already wrote all of these.
			Assert.That(reportedPointers, Does.Not.Contain("/description"));
			Assert.That(reportedPointers, Does.Not.Contain("/publisher"));
			Assert.That(reportedPointers, Does.Not.Contain("/publisher/name"));
			Assert.That(reportedPointers, Does.Not.Contain("/license"));
			Assert.That(reportedPointers, Does.Not.Contain("/compatibility"));
		});
	}
}
