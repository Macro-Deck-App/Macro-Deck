using MacroDeck.Plugin.Cli.Processes;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>
/// <c>build</c> as the command line sees it: option plumbing, the documented diagnostic shape and exit
/// codes, driven end to end through <see cref="CliEntryPoint.RunAsync" /> with the build toolchain faked.
/// The behaviour of building itself is pinned by <see cref="PluginBuilderTests" />.
/// </summary>
[TestFixture]
public class BuildCommandTests
{
	private static readonly string[] _documentedOptions =
		["--source", "--manifest", "--build-config", "--rid", "--output", "--force"];

	private string _output = null!;

	[SetUp]
	public void SetUp() => _output = Directory.CreateTempSubdirectory("macrodeck-build-cmd-out-").FullName;

	[TearDown]
	public void TearDown() => Directory.Delete(_output, recursive: true);

	[Test]
	public async Task Build_is_offered_by_the_root_command_and_documents_its_own_options()
	{
		var help = await CliRunner.Run("build", "--help");

		Assert.Multiple(() =>
		{
			Assert.That(help.ExitCode, Is.EqualTo(ExitCode.Success));

			foreach (var option in _documentedOptions)
			{
				Assert.That(help.Output, Does.Contain(option));
			}

			// No signing option: building never requires a key.
			Assert.That(help.Output, Does.Not.Contain("--private-key"));
		});
	}

	[Test]
	public async Task A_full_build_reports_the_artifact_it_wrote_and_succeeds()
	{
		var rids = ManifestFixtures.PickForeignRids(2);
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			var runner = new FakePluginBuildRunner { OnRun = BuildFixtures.ProducingOutput(project, rids) };

			var result = await CliRunner.Run(runner, "build", "--source", project, "--output", _output);

			Assert.Multiple(() =>
			{
				Assert.That(result.ExitCode, Is.EqualTo(ExitCode.Success), result.Error);
				Assert.That(result.Output, Does.Contain($"{BuildFixtures.PluginId}-{BuildFixtures.Version}"));
				Assert.That(Directory.EnumerateFiles(_output, "*.macroDeckPlugin"), Has.Exactly(1).Items);
			});
		}
		finally
		{
			Directory.Delete(project, recursive: true);
		}
	}

	[Test]
	public async Task A_failing_build_reports_the_documented_error_shape_on_stderr()
	{
		var rids = ManifestFixtures.PickForeignRids(1);
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			var runner = new FakePluginBuildRunner
			{
				Result = new Building.PluginBuildRunResult(1, string.Empty, "the toolchain said no")
			};

			var result = await CliRunner.Run(runner, "--no-color", "build", "--source", project, "--output", _output);

			Assert.Multiple(() =>
			{
				Assert.That(result.ExitCode, Is.EqualTo(ExitCode.SubjectInvalid));
				Assert.That(result.Error, Does.Match(@"(?m)^error [a-z0-9]+(-[a-z0-9]+)*: \S"));
				Assert.That(result.Error, Does.Contain("the toolchain said no"));
				Assert.That(Directory.EnumerateFiles(_output), Is.Empty);
			});
		}
		finally
		{
			Directory.Delete(project, recursive: true);
		}
	}

	[Test]
	public async Task An_unknown_option_is_a_usage_error()
	{
		var result = await CliRunner.Run(new FakePluginBuildRunner(), "build", "--not-an-option");

		Assert.That(result.ExitCode, Is.EqualTo(ExitCode.UsageError));
	}

	[Test]
	public async Task Build_never_reads_from_the_input_seam()
	{
		var rids = ManifestFixtures.PickForeignRids(1);
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			var runner = new FakePluginBuildRunner { OnRun = BuildFixtures.ProducingOutput(project, rids) };

			// CliRunner's prompt reader throws on any read, so a command that stopped to ask something -
			// "overwrite? [y/N]" - would fail here rather than hang a CI job.
			var result = await CliRunner.Run(runner, "build", "--source", project, "--output", _output);

			Assert.That(result.ExitCode, Is.EqualTo(ExitCode.Success), result.Error);
		}
		finally
		{
			Directory.Delete(project, recursive: true);
		}
	}

	[Test]
	public async Task The_build_configuration_is_looked_for_beside_the_manifest_by_default()
	{
		var rids = ManifestFixtures.PickForeignRids(1);
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			var runner = new FakePluginBuildRunner { OnRun = BuildFixtures.ProducingOutput(project, rids) };

			// --source elsewhere, manifest (and therefore the recipe 'new' wrote beside it) in the project.
			var result = await CliRunner.Run(runner,
				"build",
				"--source",
				project,
				"--manifest",
				Path.Combine(project, "manifest.json"),
				"--output",
				_output);

			Assert.That(result.ExitCode, Is.EqualTo(ExitCode.Success), result.Error);
		}
		finally
		{
			Directory.Delete(project, recursive: true);
		}
	}

	[Test]
	public void An_executable_that_cannot_be_launched_is_a_launch_exception_not_a_platform_exception()
	{
		// The contract both 'new' and 'build' translate: the process helper reports "could not start at all"
		// as one neutral exception, rather than leaking a platform-specific Win32Exception that each caller
		// would have to recognise for itself.
		var runner = new Building.ProcessPluginBuildRunner();

		var exception = Assert.ThrowsAsync<ProcessLaunchException>(() =>
			runner.RunAsync("macrodeck-definitely-not-a-real-tool", [], Path.GetTempPath(), CancellationToken.None));

		Assert.That(exception!.FileName, Is.EqualTo("macrodeck-definitely-not-a-real-tool"));
	}
}
