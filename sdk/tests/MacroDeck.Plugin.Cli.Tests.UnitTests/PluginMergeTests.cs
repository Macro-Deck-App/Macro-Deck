using System.IO.Compression;
using System.Text.Json.Nodes;
using MacroDeck.Plugin.Cli.Building;
using MacroDeck.Plugin.Cli.Manifests;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

[TestFixture]
public class PluginMergeTests
{
	private string _work = null!;

	[SetUp]
	public void SetUp() => _work = Directory.CreateTempSubdirectory("macrodeck-plugin-merge-tests-").FullName;

	[TearDown]
	public void TearDown() => Directory.Delete(_work, recursive: true);

	[Test]
	public async Task Packages_built_per_runtime_identifier_merge_into_the_package_a_full_build_produces()
	{
		var rids = ManifestFixtures.PickForeignRids(3);
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			var full = await BuildAsync(project, rids, rid: null, Path.Combine(_work, "full"));
			var parts = new List<string>();

			foreach (var rid in rids)
			{
				parts.Add(await BuildAsync(project, rids, rid, Path.Combine(_work, rid)));
			}

			var result = await CliRunner.Run(["merge", .. parts, "--output", Path.Combine(_work, "merged")]);
			var merged = Path.Combine(_work, "merged", $"{BuildFixtures.PluginId}-{BuildFixtures.Version}.macroDeckPlugin");
			var inspection = await ArtifactReaders.ArtifactReader.Inspect(merged);

			Assert.Multiple(() =>
			{
				Assert.That(result.ExitCode, Is.EqualTo(ExitCode.Success), result.Error);
				Assert.That(Entries(merged), Is.EquivalentTo(Entries(full)));
				Assert.That(inspection.Success, Is.True, inspection.ErrorMessage);
				Assert.That(inspection.Manifest!.Entrypoints.Keys, Is.EquivalentTo(rids));
			});
		}
		finally
		{
			Directory.Delete(project, recursive: true);
		}
	}

	[Test]
	public async Task The_same_runtime_identifier_in_two_packages_is_refused()
	{
		var rids = ManifestFixtures.PickForeignRids(2);
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			var first = await BuildAsync(project, rids, rids[0], Path.Combine(_work, "a"));
			var second = await BuildAsync(project, rids, rids[0], Path.Combine(_work, "b"));

			var result = await CliRunner.Run("merge", first, second, "--output", Path.Combine(_work, "merged"));

			AssertRefused(result, "duplicate-rid");
		}
		finally
		{
			Directory.Delete(project, recursive: true);
		}
	}

	[Test]
	public async Task Packages_of_different_versions_are_refused()
	{
		var rids = ManifestFixtures.PickForeignRids(2);
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			var first = await BuildAsync(project, rids, rids[0], Path.Combine(_work, "a"));
			SetManifestVersion(project, "2.0.0");
			var second = await BuildAsync(project, rids, rids[1], Path.Combine(_work, "b"));

			var result = await CliRunner.Run("merge", first, second, "--output", Path.Combine(_work, "merged"));

			AssertRefused(result, "identity-mismatch");
		}
		finally
		{
			Directory.Delete(project, recursive: true);
		}
	}

	[Test]
	public async Task A_shared_file_that_differs_between_packages_is_refused()
	{
		var rids = ManifestFixtures.PickForeignRids(2);
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			await File.WriteAllTextAsync(Path.Combine(project, "macrodeck-build.json"),
				BuildFixtures.BuildConfigJson(rids, ["README.md"]));
			var first = await BuildAsync(project, rids, rids[0], Path.Combine(_work, "a"));
			await File.WriteAllTextAsync(Path.Combine(project, "README.md"), "a different readme");
			var second = await BuildAsync(project, rids, rids[1], Path.Combine(_work, "b"));

			var result = await CliRunner.Run("merge", first, second, "--output", Path.Combine(_work, "merged"));

			AssertRefused(result, "file-conflict");
		}
		finally
		{
			Directory.Delete(project, recursive: true);
		}
	}

	[Test]
	public async Task A_package_whose_contents_no_longer_match_its_manifest_is_refused()
	{
		var rids = ManifestFixtures.PickForeignRids(2);
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			var first = await BuildAsync(project, rids, rids[0], Path.Combine(_work, "a"));
			var second = await BuildAsync(project, rids, rids[1], Path.Combine(_work, "b"));

			using (var archive = ZipFile.Open(second, ZipArchiveMode.Update))
			{
				var entryName = BuildFixtures.EntrypointPath(rids[1]);
				archive.GetEntry(entryName)!.Delete();
				await using var writer = new StreamWriter(archive.CreateEntry(entryName).Open());
				await writer.WriteAsync("replaced after the build");
			}

			var result = await CliRunner.Run("merge", first, second, "--output", Path.Combine(_work, "merged"));

			AssertRefused(result, "hash-mismatch");
		}
		finally
		{
			Directory.Delete(project, recursive: true);
		}
	}

	[Test]
	public async Task Packages_that_bundle_icon_packs_differently_are_refused()
	{
		var rids = ManifestFixtures.PickForeignRids(2);
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			IconPackFixtures.Write(Path.Combine(project, "icon-packs", "logos.macroDeckIconPack"), "Logos", ["obs"]);
			SetBundledIconPackKey(project, "logos");
			var first = await BuildAsync(project, rids, rids[0], Path.Combine(_work, "a"));
			SetBundledIconPackKey(project, "brands");
			var second = await BuildAsync(project, rids, rids[1], Path.Combine(_work, "b"));

			var result = await CliRunner.Run("merge", first, second, "--output", Path.Combine(_work, "merged"));

			AssertRefused(result, "manifest-mismatch");
		}
		finally
		{
			Directory.Delete(project, recursive: true);
		}
	}

	[Test]
	public async Task An_existing_artifact_is_only_overwritten_with_force()
	{
		var rids = ManifestFixtures.PickForeignRids(1);
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			var part = await BuildAsync(project, rids, rids[0], Path.Combine(_work, "a"));
			var output = Path.Combine(_work, "merged");

			var created = await CliRunner.Run("merge", part, "--output", output);
			var refused = await CliRunner.Run("merge", part, "--output", output);
			var forced = await CliRunner.Run("merge", part, "--output", output, "--force");

			Assert.Multiple(() =>
			{
				Assert.That(created.ExitCode, Is.EqualTo(ExitCode.Success), created.Error);
				Assert.That(refused.ExitCode, Is.EqualTo(ExitCode.UsageError));
				Assert.That(refused.Error, Does.Contain("output-exists"));
				Assert.That(forced.ExitCode, Is.EqualTo(ExitCode.Success), forced.Error);
			});
		}
		finally
		{
			Directory.Delete(project, recursive: true);
		}
	}

	private void AssertRefused((string Output, string Error, int ExitCode) result, string code)
	{
		Assert.Multiple(() =>
		{
			Assert.That(result.ExitCode, Is.EqualTo(ExitCode.SubjectInvalid), result.Error);
			Assert.That(result.Error, Does.Contain($"error {code}:"));
			Assert.That(Directory.Exists(Path.Combine(_work, "merged")) &&
				Directory.EnumerateFiles(Path.Combine(_work, "merged")).Any(), Is.False);
		});
	}

	private async Task<string> BuildAsync(string project, IReadOnlyList<string> rids, string? rid, string output)
	{
		var result = await PluginBuilder.BuildAsync(new PluginBuildRequest
		{
			SourceDirectory = project,
			ManifestPath = Path.Combine(project, "manifest.json"),
			BuildConfigPath = Path.Combine(project, "macrodeck-build.json"),
			Rid = rid,
			OutputDirectory = output,
			StagingRoot = _work
		},
			new FakePluginBuildRunner { OnRun = BuildFixtures.ProducingOutput(project, rids) });

		Assert.That(result.Success, Is.True, result.FailureMessage);

		return result.Pack!.OutputPath!;
	}

	private static void SetManifestVersion(string project, string version)
	{
		var path = Path.Combine(project, "manifest.json");
		var manifest = JsonNode.Parse(File.ReadAllText(path))!;
		manifest["version"] = version;
		File.WriteAllText(path, manifest.ToJsonString());
	}

	private static void SetBundledIconPackKey(string project, string key)
	{
		var path = Path.Combine(project, "manifest.json");
		var manifest = JsonNode.Parse(File.ReadAllText(path))!;
		manifest["bundledIconPacks"] = new JsonArray(new JsonObject
			{ ["key"] = key, ["path"] = "icon-packs/logos.macroDeckIconPack" });
		File.WriteAllText(path, manifest.ToJsonString());
	}

	private static List<string> Entries(string artifactPath)
	{
		using var archive = ZipFile.OpenRead(artifactPath);

		return archive.Entries.Select(entry => entry.FullName).ToList();
	}
}
