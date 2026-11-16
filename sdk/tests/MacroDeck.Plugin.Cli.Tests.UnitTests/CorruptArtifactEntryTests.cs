using MacroDeck.Plugin.Cli.Building;
using MacroDeck.Plugin.Cli.Manifests;
using MacroDeck.Plugin.Cli.Tests.UnitTests.Support;
using MacroDeck.Plugin.Packaging.Artifacts;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

[TestFixture]
public class CorruptArtifactEntryTests
{
	private const string CorruptEntrySentence = "is not a readable .macroDeckPlugin artifact: an entry is corrupt (";

	private string _work = null!;
	private string _project = null!;
	private IReadOnlyList<string> _rids = null!;

	[SetUp]
	public void SetUp()
	{
		_work = Directory.CreateTempSubdirectory("macrodeck-plugin-corrupt-entry-tests-").FullName;
		_rids = ManifestFixtures.PickForeignRids(1);
		_project = BuildFixtures.WriteProject(_rids);
	}

	[TearDown]
	public void TearDown()
	{
		Directory.Delete(_project, true);
		Directory.Delete(_work, true);
	}

	[TestCase("inspect")]
	[TestCase("validate")]
	public async Task A_package_with_a_corrupt_entry_is_reported_as_an_invalid_archive(string command)
	{
		var artifact = await CorruptPackageAsync();

		var result = await CliRunner.Run(command, "--artifact", artifact, "--no-color");

		AssertReported(result, "not-an-artifact", artifact);
	}

	[Test]
	public async Task Running_a_package_with_a_corrupt_entry_reports_it_as_unreadable()
	{
		var artifact = await CorruptPackageAsync();

		var result = await CliRunner.Run("run", "--artifact", artifact, "--stub-host", "--no-color");

		AssertReported(result, "artifact-unreadable", artifact);
	}

	[Test]
	public async Task Testing_a_package_with_a_corrupt_entry_reports_that_it_could_not_be_launched()
	{
		var artifact = await CorruptPackageAsync();

		var result = await CliRunner.Run("test", "--artifact", artifact, "--no-color");

		AssertReported(result, "subject-launch-failed", artifact);
	}

	[Test]
	public async Task Previewing_a_package_with_a_corrupt_entry_reports_it_as_unreadable()
	{
		var artifact = await CorruptPackageAsync();

		var result = await RunPreviewAsync(artifact);

		AssertReported(result, "artifact-unreadable", artifact);
	}

	[Test]
	public async Task Previewing_a_package_without_an_entrypoint_for_this_machine_reports_it_as_unreadable()
	{
		var artifact = await BuildAsync();

		var (_, error, exitCode) = await RunPreviewAsync(artifact);

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.InputUnreadable), error);
			Assert.That(error, Does.Contain("error artifact-unreadable:"));
			Assert.That(error, Does.Not.Contain("internal-error"));
		});
	}

	[Test]
	public async Task Extracting_a_package_with_a_corrupt_entry_fails_as_an_invalid_archive_and_leaves_nothing()
	{
		var artifact = await CorruptPackageAsync();
		var target = Path.Combine(_work, "extracted");

		var result = await ArtifactReaders.ArtifactReader.ExtractTo(artifact, target);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.InvalidArchive));
			Assert.That(Directory.Exists(target), Is.False);
		});
	}

	private static void AssertReported((string Output, string Error, int ExitCode) result, string code, string artifact)
	{
		var printed = result.Output + result.Error;

		Assert.Multiple(() =>
		{
			Assert.That(result.ExitCode, Is.EqualTo(ExitCode.InputUnreadable), printed);
			Assert.That(printed, Does.Contain($"error {code}: '{artifact}' {CorruptEntrySentence}"));
			Assert.That(printed, Does.Not.Contain("internal-error"));
			Assert.That(printed, Does.Not.Contain("   at "));
		});
	}

	private async Task<(string Output, string Error, int ExitCode)> RunPreviewAsync(string artifact)
	{
		var browser = Path.Combine(_work, "browser");
		await File.WriteAllTextAsync(browser, string.Empty);

		return await CliRunner.RunPreview(new FakeScreenshotter().Factory,
			"preview",
			"render",
			"--artifact",
			artifact,
			"--browser",
			browser,
			"--output",
			Path.Combine(_work, "previews"),
			"--no-color");
	}

	private async Task<string> CorruptPackageAsync()
	{
		var artifact = await BuildAsync();
		await CorruptArtifacts.CorruptFirstEntryAsync(artifact);

		return artifact;
	}

	private async Task<string> BuildAsync()
	{
		var result = await PluginBuilder.BuildAsync(new PluginBuildRequest
			{
				SourceDirectory = _project,
				ManifestPath = Path.Combine(_project, "manifest.json"),
				BuildConfigPath = Path.Combine(_project, "macrodeck-build.json"),
				Rid = _rids[0],
				OutputDirectory = Path.Combine(_work, "package"),
				StagingRoot = _work
			},
			new FakePluginBuildRunner { OnRun = BuildFixtures.ProducingOutput(_project, _rids) });

		Assert.That(result.Success, Is.True, result.FailureMessage);

		return result.Pack!.OutputPath!;
	}
}
