using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Plugin.Cli.Building;
using MacroDeck.Plugin.Cli.Manifests;
using MacroDeck.Plugin.Cli.Packing;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>
/// <c>validate --level</c> (issue #611 part 2): the three <see cref="PluginManifestValidationLevel" />
/// values, their implied defaults, and the payload/entrypoint-layout checks that only fire at
/// <see cref="PluginManifestValidationLevel.Package" /> and above.
/// </summary>
[TestFixture]
public class ManifestValidationLevelTests
{
	private static readonly string[] _sixPublicationPointers =
		["/publisher", "/description", "/icon", "/license", "/repository", "/compatibility"];

	private static readonly string[] _filesAndSignaturePointers = ["/files", "/signature"];

	[Test]
	public async Task Validating_a_minimal_manifest_is_still_clean_and_silent_at_development_level()
	{
		var directory = ManifestFixtures.WriteValidManifestDirectory();

		try
		{
			var (output, _, exitCode) =
				await CliRunner.Run("validate",
					"--manifest",
					Path.Combine(directory, "manifest.json"),
					"--output",
					"json",
					"--no-color");

			using var document = JsonDocument.Parse(output);

			Assert.Multiple(() =>
			{
				Assert.That(exitCode, Is.EqualTo(ExitCode.Success));
				Assert.That(document.RootElement.GetProperty("valid").GetBoolean(), Is.True);
				Assert.That(document.RootElement.GetProperty("problems").GetArrayLength(), Is.EqualTo(0));
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public async Task An_absent_level_flag_takes_the_level_from_the_subject()
	{
		var directory = PublicationIncompleteDirectory();
		var artifactPath = Path.Combine(Path.GetTempPath(),
			$"macrodeck-plugin-cli-tests-{Guid.NewGuid():N}.macroDeckPlugin");

		try
		{
			var pack = await PluginPacker.PackAsync(directory,
				Path.Combine(directory, "manifest.json"),
				_ => artifactPath,
				force: false);
			Assert.That(pack.Success, Is.True, pack.FailureMessage);

			var (manifestOutput, _, manifestExit) =
				await CliRunner.Run("validate",
					"--manifest",
					Path.Combine(directory, "manifest.json"),
					"--output",
					"json",
					"--no-color");
			var (directoryOutput, _, directoryExit) =
				await CliRunner.Run("validate", "--directory", directory, "--output", "json", "--no-color");
			var (artifactOutput, _, artifactExit) =
				await CliRunner.Run("validate", "--artifact", artifactPath, "--output", "json", "--no-color");

			using var manifestDocument = JsonDocument.Parse(manifestOutput);
			using var directoryDocument = JsonDocument.Parse(directoryOutput);
			using var artifactDocument = JsonDocument.Parse(artifactOutput);

			var artifactPointers = artifactDocument.RootElement.GetProperty("problems")
				.EnumerateArray()
				.Select(problem => problem.GetProperty("pointer").GetString())
				.ToList();

			Assert.Multiple(() =>
			{
				Assert.That(manifestExit, Is.EqualTo(ExitCode.Success));
				Assert.That(manifestDocument.RootElement.GetProperty("problems").GetArrayLength(), Is.EqualTo(0));

				Assert.That(directoryExit, Is.EqualTo(ExitCode.Success));
				Assert.That(directoryDocument.RootElement.GetProperty("problems").GetArrayLength(), Is.EqualTo(0));

				Assert.That(artifactExit, Is.EqualTo(ExitCode.Success));
				Assert.That(artifactPointers, Is.EquivalentTo(_sixPublicationPointers));

				foreach (var problem in artifactDocument.RootElement.GetProperty("problems").EnumerateArray())
				{
					Assert.That(problem.GetProperty("severity").GetString(), Is.EqualTo("warning"));
					Assert.That(problem.GetProperty("code").GetString(), Is.EqualTo("publication-metadata-missing"));
					Assert.That(problem.GetProperty("requiredBy").GetString(), Is.EqualTo("publication"));
				}
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
			if (File.Exists(artifactPath))
			{
				File.Delete(artifactPath);
			}
		}
	}

	[Test]
	public async Task An_explicit_level_flag_overrides_the_subject_default_in_both_directions()
	{
		var directory = PublicationIncompleteDirectory();
		var artifactPath = Path.Combine(Path.GetTempPath(),
			$"macrodeck-plugin-cli-tests-{Guid.NewGuid():N}.macroDeckPlugin");

		try
		{
			var pack = await PluginPacker.PackAsync(directory,
				Path.Combine(directory, "manifest.json"),
				_ => artifactPath,
				force: false);
			Assert.That(pack.Success, Is.True, pack.FailureMessage);

			var (manifestOutput, _, manifestExit) =
				await CliRunner.Run("validate",
					"--manifest",
					Path.Combine(directory, "manifest.json"),
					"--level",
					"publication",
					"--output",
					"json",
					"--no-color");
			var (_, _, artifactExit) =
				await CliRunner.Run("validate",
					"--artifact",
					artifactPath,
					"--level",
					"development",
					"--output",
					"json",
					"--no-color");

			using var manifestDocument = JsonDocument.Parse(manifestOutput);

			Assert.Multiple(() =>
			{
				Assert.That(manifestExit, Is.EqualTo(ExitCode.SubjectInvalid));
				Assert.That(manifestDocument.RootElement.GetProperty("problems").GetArrayLength(), Is.EqualTo(6));

				foreach (var problem in manifestDocument.RootElement.GetProperty("problems").EnumerateArray())
				{
					Assert.That(problem.GetProperty("severity").GetString(), Is.EqualTo("error"));
				}

				Assert.That(artifactExit, Is.EqualTo(ExitCode.Success));
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
			if (File.Exists(artifactPath))
			{
				File.Delete(artifactPath);
			}
		}
	}

	[Test]
	public async Task A_publication_problem_names_the_field_and_the_level_that_requires_it()
	{
		var directory = AlmostPublicationCompleteDirectory(out _);

		try
		{
			var (output, _, exitCode) =
				await CliRunner.Run("validate",
					"--manifest",
					Path.Combine(directory, "manifest.json"),
					"--level",
					"publication",
					"--output",
					"json",
					"--no-color");

			using var document = JsonDocument.Parse(output);
			var problems = document.RootElement.GetProperty("problems").EnumerateArray().ToList();

			Assert.Multiple(() =>
			{
				Assert.That(exitCode, Is.EqualTo(ExitCode.SubjectInvalid));
				Assert.That(problems, Has.Count.EqualTo(1));
				Assert.That(problems[0].GetProperty("severity").GetString(), Is.EqualTo("error"));
				Assert.That(problems[0].GetProperty("code").GetString(), Is.EqualTo("publication-metadata-missing"));
				Assert.That(problems[0].GetProperty("pointer").GetString(), Is.EqualTo("/license"));
				Assert.That(problems[0].GetProperty("requiredBy").GetString(), Is.EqualTo("publication"));
				Assert.That(problems[0].GetProperty("message").GetString(), Is.Not.Null.And.Not.Empty);
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public async Task A_declared_entrypoint_missing_from_the_packaged_content_is_a_package_level_error()
	{
		var rids = ManifestFixtures.PickForeignRids(2);
		var present = rids[0];
		var missing = rids[1];

		var directory = Directory.CreateTempSubdirectory("macrodeck-plugin-cli-tests-").FullName;
		WriteRuntimeEntrypoint(directory, present, create: true);
		WriteRuntimeEntrypoint(directory, missing, create: false);

		var json = $$"""
					 {
					 	"manifestVersion": 1,
					 	"id": "{{ManifestFixtures.PluginId}}",
					 	"name": "Test Plugin",
					 	"version": "{{ManifestFixtures.Version}}",
					 	"entrypoints": {
					 		"{{present}}": { "executable": "{{RuntimeEntrypointPath(present)}}" },
					 		"{{missing}}": { "executable": "{{RuntimeEntrypointPath(missing)}}" }
					 	}
					 }
					 """;
		File.WriteAllText(Path.Combine(directory, "manifest.json"), json);

		var artifactPath = Path.Combine(Path.GetTempPath(),
			$"macrodeck-plugin-cli-tests-{Guid.NewGuid():N}.macroDeckPlugin");

		try
		{
			var pack = await PluginPacker.PackAsync(directory,
				Path.Combine(directory, "manifest.json"),
				_ => artifactPath,
				force: false);
			Assert.That(pack.Success, Is.True, pack.FailureMessage);

			var result = await ManifestValidator.ValidateArtifactAsync(artifactPath);

			Assert.Multiple(() =>
			{
				Assert.That(result.ExitCode, Is.EqualTo(ExitCode.SubjectInvalid));

				var entrypointProblems = result.Problems.Where(p => p.Code == "entrypoint-not-packed").ToList();
				Assert.That(entrypointProblems, Has.Count.EqualTo(1));
				Assert.That(entrypointProblems[0].Severity, Is.EqualTo(ManifestProblemSeverity.Error));
				Assert.That(entrypointProblems[0].Pointer, Is.EqualTo($"/entrypoints/{missing}/executable"));
				Assert.That(entrypointProblems[0].Level, Is.EqualTo(PluginManifestValidationLevel.Package));

				Assert.That(result.Problems,
					Has.None.Matches<ManifestProblem>(p => p.Pointer == $"/entrypoints/{present}/executable"));
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
			if (File.Exists(artifactPath))
			{
				File.Delete(artifactPath);
			}
		}
	}

	[Test]
	public async Task A_declared_icon_missing_from_the_packaged_content_is_a_package_level_error()
	{
		var rid = ManifestFixtures.PickForeignRids(1)[0];

		var directory = Directory.CreateTempSubdirectory("macrodeck-plugin-cli-tests-").FullName;
		WriteRuntimeEntrypoint(directory, rid, create: true);

		var json = $$"""
					 {
					 	"manifestVersion": 1,
					 	"id": "{{ManifestFixtures.PluginId}}",
					 	"name": "Test Plugin",
					 	"version": "{{ManifestFixtures.Version}}",
					 	"icon": "assets/icon.svg",
					 	"entrypoints": {
					 		"{{rid}}": { "executable": "{{RuntimeEntrypointPath(rid)}}" }
					 	}
					 }
					 """;
		File.WriteAllText(Path.Combine(directory, "manifest.json"), json);

		var artifactPath = Path.Combine(Path.GetTempPath(),
			$"macrodeck-plugin-cli-tests-{Guid.NewGuid():N}.macroDeckPlugin");

		try
		{
			var pack = await PluginPacker.PackAsync(directory,
				Path.Combine(directory, "manifest.json"),
				_ => artifactPath,
				force: false);
			Assert.That(pack.Success, Is.True, pack.FailureMessage);

			var result = await ManifestValidator.ValidateArtifactAsync(artifactPath);

			Assert.Multiple(() =>
			{
				Assert.That(result.ExitCode, Is.EqualTo(ExitCode.SubjectInvalid));

				var iconProblems = result.Problems.Where(p => p.Code == "icon-declared-not-present").ToList();
				Assert.That(iconProblems, Has.Count.EqualTo(1));
				Assert.That(iconProblems[0].Severity, Is.EqualTo(ManifestProblemSeverity.Error));
				Assert.That(iconProblems[0].Pointer, Is.EqualTo("/icon"));
				Assert.That(iconProblems[0].Level, Is.EqualTo(PluginManifestValidationLevel.Package));
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
			if (File.Exists(artifactPath))
			{
				File.Delete(artifactPath);
			}
		}
	}

	[Test]
	public async Task Payload_checks_never_fire_against_an_unbuilt_source_tree()
	{
		var rid = ManifestFixtures.PickForeignRids(1)[0];

		var directory = Directory.CreateTempSubdirectory("macrodeck-plugin-cli-tests-").FullName;
		File.WriteAllText(Path.Combine(directory, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

		var json = $$"""
					 {
					 	"manifestVersion": 1,
					 	"id": "{{ManifestFixtures.PluginId}}",
					 	"name": "Test Plugin",
					 	"version": "{{ManifestFixtures.Version}}",
					 	"icon": "assets/icon.svg",
					 	"entrypoints": {
					 		"{{rid}}": { "executable": "{{RuntimeEntrypointPath(rid)}}" }
					 	}
					 }
					 """;
		File.WriteAllText(Path.Combine(directory, "manifest.json"), json);

		try
		{
			var developmentResult = await ManifestValidator.ValidateDirectoryAsync(directory);
			var publicationResult =
				await ManifestValidator.ValidateDirectoryAsync(directory, PluginManifestValidationLevel.Publication);

			Assert.Multiple(() =>
			{
				Assert.That(developmentResult.Problems,
					Has.None.Matches<ManifestProblem>(p =>
						p.Code is "entrypoint-not-packed" or "icon-declared-not-present"));

				Assert.That(publicationResult.Problems,
					Has.None.Matches<ManifestProblem>(p =>
						p.Code is "entrypoint-not-packed" or "icon-declared-not-present"));

				// Metadata gaps alone are still enough to fail publication level.
				Assert.That(publicationResult.ExitCode, Is.EqualTo(ExitCode.SubjectInvalid));
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public async Task
		A_hand_authored_files_or_signature_block_warns_on_a_source_manifest_and_is_silent_on_an_extracted_directory()
	{
		var rid = ManifestFixtures.PickForeignRids(1)[0];

		var sourceDirectory = Directory.CreateTempSubdirectory("macrodeck-plugin-cli-tests-").FullName;
		File.WriteAllText(Path.Combine(sourceDirectory, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

		var staleBytes = "stale"u8.ToArray();
		File.WriteAllBytes(Path.Combine(sourceDirectory, "stale.bin"), staleBytes);
		var staleDigest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(staleBytes));

		var sourceJson = $$"""
						   {
						   	"manifestVersion": 1,
						   	"id": "{{ManifestFixtures.PluginId}}",
						   	"name": "Test Plugin",
						   	"version": "{{ManifestFixtures.Version}}",
						   	"description": "A plugin.",
						   	"icon": "assets/icon.svg",
						   	"publisher": { "name": "Example Publisher" },
						   	"license": "MIT",
						   	"repository": "https://example.com/repo",
						   	"compatibility": { "macroDeck": ">=3.0.0" },
						   	"entrypoints": {
						   		"{{rid}}": { "executable": "{{RuntimeEntrypointPath(rid)}}" }
						   	},
						   	"files": [
						   		{ "path": "stale.bin", "sha256": "{{staleDigest}}", "size": {{staleBytes.Length}} }
						   	],
						   	"signature": { "algorithm": "ed25519", "keyId": "fixture-key", "value": "AA==" }
						   }
						   """;
		File.WriteAllText(Path.Combine(sourceDirectory, "manifest.json"), sourceJson);

		try
		{
			foreach (var level in Enum.GetValues<PluginManifestValidationLevel>())
			{
				var result = await ManifestValidator.ValidateManifestFileAsync(
					Path.Combine(sourceDirectory, "manifest.json"),
					level);

				Assert.Multiple(() =>
				{
					Assert.That(result.ExitCode,
						Is.EqualTo(ExitCode.Success),
						$"level {level}: {string.Join(", ", result.Problems.Select(p => p.Message))}");

					var generatedProblems = result.Problems.Where(p => p.Code == "generated-field-authored").ToList();
					Assert.That(generatedProblems, Has.Count.EqualTo(2), $"level {level}");
					Assert.That(generatedProblems.Select(p => p.Pointer), Is.EquivalentTo(_filesAndSignaturePointers));
					Assert.That(generatedProblems,
						Has.All.Matches<ManifestProblem>(p => p.Severity == ManifestProblemSeverity.Warning));
				});
			}
		}
		finally
		{
			Directory.Delete(sourceDirectory, recursive: true);
		}

		// (b) An extracted directory: same shape, but a real matching payload and no project file.
		var extractedDirectory = Directory.CreateTempSubdirectory("macrodeck-plugin-cli-tests-").FullName;
		var payloadPath = Path.Combine(extractedDirectory,
			RuntimeEntrypointPath(rid).Replace('/', Path.DirectorySeparatorChar));
		Directory.CreateDirectory(Path.GetDirectoryName(payloadPath)!);
		File.WriteAllBytes(payloadPath, "payload"u8.ToArray());
		var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(payloadPath)));

		var extractedJson = $$"""
							  {
							  	"manifestVersion": 1,
							  	"id": "{{ManifestFixtures.PluginId}}",
							  	"name": "Test Plugin",
							  	"version": "{{ManifestFixtures.Version}}",
							  	"description": "A plugin.",
							  	"publisher": { "name": "Example Publisher" },
							  	"license": "MIT",
							  	"repository": "https://example.com/repo",
							  	"compatibility": { "macroDeck": ">=3.0.0" },
							  	"entrypoints": {
							  		"{{rid}}": { "executable": "{{RuntimeEntrypointPath(rid)}}" }
							  	},
							  	"files": [
							  		{ "path": "{{RuntimeEntrypointPath(rid)}}", "sha256": "{{digest}}", "size": {{"payload"u8.Length}} }
							  	]
							  }
							  """;
		File.WriteAllText(Path.Combine(extractedDirectory, "manifest.json"), extractedJson);

		try
		{
			var result =
				await ManifestValidator.ValidateDirectoryAsync(extractedDirectory,
					PluginManifestValidationLevel.Publication);

			Assert.That(result.Problems,
				Has.None.Matches<ManifestProblem>(p => p.Pointer == "/files" || p.Pointer == "/signature"));
		}
		finally
		{
			Directory.Delete(extractedDirectory, recursive: true);
		}
	}

	[Test]
	public async Task Validate_and_build_agree_on_two_entrypoints_resolving_into_one_directory()
	{
		var rids = ManifestFixtures.PickForeignRids(2);
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			await File.WriteAllTextAsync(Path.Combine(project, "manifest.json"),
				BuildFixtures.ManifestJson(rids).Replace($"runtimes/{rids[1]}/", $"runtimes/{rids[0]}/"));

			var validation =
				await ManifestValidator.ValidateDirectoryAsync(project, PluginManifestValidationLevel.Package);

			var runner = new FakePluginBuildRunner { OnRun = BuildFixtures.ProducingOutput(project, rids) };
			var buildResult = await PluginBuilder.BuildAsync(new PluginBuildRequest
				{
					SourceDirectory = project,
					ManifestPath = Path.Combine(project, "manifest.json"),
					BuildConfigPath = Path.Combine(project, "macrodeck-build.json"),
					OutputDirectory = Path.Combine(project, "out")
				},
				runner);

			Assert.Multiple(() =>
			{
				Assert.That(validation.ExitCode, Is.EqualTo(ExitCode.SubjectInvalid));

				var layoutProblems = validation.Problems.Where(p => p.Code == "entrypoint-layout-invalid").ToList();
				Assert.That(layoutProblems, Is.Not.Empty);
				string[] expectedPointers =
					[$"/entrypoints/{rids[0]}/executable", $"/entrypoints/{rids[1]}/executable"];
				Assert.That(layoutProblems.Select(p => p.Pointer), Is.SupersetOf(expectedPointers));

				Assert.That(buildResult.FailureReason, Is.EqualTo(PluginBuildFailureReason.EntrypointLayoutInvalid));
				Assert.That(runner.Invocations, Is.Empty);
			});
		}
		finally
		{
			Directory.Delete(project, recursive: true);
		}
	}

	[Test]
	public async Task An_entrypoint_at_the_package_root_is_refused_by_validate_and_by_build_alike()
	{
		var rid = ManifestFixtures.PickForeignRids(1)[0];
		var project = BuildFixtures.WriteProject([rid]);

		try
		{
			await File.WriteAllTextAsync(Path.Combine(project, "manifest.json"),
				BuildFixtures.ManifestJson([rid]).Replace($"runtimes/{rid}/", string.Empty));

			var packageResult =
				await ManifestValidator.ValidateDirectoryAsync(project, PluginManifestValidationLevel.Package);
			var developmentResult = await ManifestValidator.ValidateDirectoryAsync(project);

			var runner = new FakePluginBuildRunner { OnRun = BuildFixtures.ProducingOutput(project, [rid]) };
			var buildResult = await PluginBuilder.BuildAsync(new PluginBuildRequest
				{
					SourceDirectory = project,
					ManifestPath = Path.Combine(project, "manifest.json"),
					BuildConfigPath = Path.Combine(project, "macrodeck-build.json"),
					OutputDirectory = Path.Combine(project, "out")
				},
				runner);

			Assert.Multiple(() =>
			{
				Assert.That(packageResult.ExitCode, Is.EqualTo(ExitCode.SubjectInvalid));
				Assert.That(packageResult.Problems,
					Has.Some.Matches<ManifestProblem>(p =>
						p.Code == "entrypoint-layout-invalid" && p.Pointer == $"/entrypoints/{rid}/executable"));

				Assert.That(buildResult.FailureReason, Is.EqualTo(PluginBuildFailureReason.EntrypointLayoutInvalid));

				Assert.That(developmentResult.ExitCode, Is.EqualTo(ExitCode.Success));
				Assert.That(developmentResult.Problems, Is.Empty);
			});
		}
		finally
		{
			Directory.Delete(project, recursive: true);
		}
	}

	[Test]
	public async Task Build_and_pack_warn_about_publication_gaps_without_changing_their_exit_code()
	{
		var rids = ManifestFixtures.PickForeignRids(2);
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			var manifestWithoutIcon = JsonNode.Parse(BuildFixtures.ManifestJson(rids))!.AsObject();
			manifestWithoutIcon.Remove("icon");
			await File.WriteAllTextAsync(Path.Combine(project, "manifest.json"), manifestWithoutIcon.ToJsonString());

			var packOutput = Path.Combine(project, "pack-output", "plugin.macroDeckPlugin");
			var packResult = await PluginPacker.PackAsync(project,
				Path.Combine(project, "manifest.json"),
				_ => packOutput,
				force: false);

			var buildOutputDirectory = Path.Combine(project, "build-output");
			Directory.CreateDirectory(buildOutputDirectory);
			var runner = new FakePluginBuildRunner { OnRun = BuildFixtures.ProducingOutput(project, rids) };
			var buildResult = await PluginBuilder.BuildAsync(new PluginBuildRequest
				{
					SourceDirectory = project,
					ManifestPath = Path.Combine(project, "manifest.json"),
					BuildConfigPath = Path.Combine(project, "macrodeck-build.json"),
					OutputDirectory = buildOutputDirectory
				},
				runner);

			var expectedPointers = new[]
				{ "'publisher'", "'description'", "'icon'", "'license'", "'repository'", "'compatibility'" };

			Assert.Multiple(() =>
			{
				Assert.That(packResult.Success, Is.True, packResult.FailureMessage);
				Assert.That(File.Exists(packOutput), Is.True);

				var packPublicationWarnings =
					packResult.Warnings.Where(w => w.Code == "publication-metadata-missing").ToList();
				Assert.That(packPublicationWarnings, Has.Count.EqualTo(6));
				foreach (var expected in expectedPointers)
				{
					Assert.That(packPublicationWarnings,
						Has.Some.Matches<CliDiagnostic>(w => w.Message.Contains(expected)));
				}

				Assert.That(buildResult.Success, Is.True, buildResult.FailureMessage);
				Assert.That(buildResult.Pack!.OutputPath, Is.Not.Null);
				Assert.That(File.Exists(buildResult.Pack!.OutputPath!), Is.True);

				var buildPublicationWarnings =
					buildResult.Warnings.Where(w => w.Code == "publication-metadata-missing").ToList();
				Assert.That(buildPublicationWarnings, Has.Count.EqualTo(6));
			});
		}
		finally
		{
			Directory.Delete(project, recursive: true);
		}
	}

	[Test]
	public async Task An_unknown_level_token_is_a_usage_error_not_a_verdict_about_the_plugin()
	{
		var directory = ManifestFixtures.WriteValidManifestDirectory();

		try
		{
			var (output, error, exitCode) =
				await CliRunner.Run("validate",
					"--manifest",
					Path.Combine(directory, "manifest.json"),
					"--level",
					"strict",
					"--no-color");

			var combined = output + error;

			Assert.Multiple(() =>
			{
				Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
				Assert.That(combined, Does.Contain("--level"));
				Assert.That(combined, Does.Contain("development"));
				Assert.That(combined, Does.Contain("package"));
				Assert.That(combined, Does.Contain("publication"));

				// Nothing about the manifest itself was ever judged.
				Assert.That(combined, Does.Not.Contain("error(s)"));
				Assert.That(combined, Does.Not.Contain(ManifestFixtures.PluginId));
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	/// <summary>A directory declaring exactly the five required fields plus one entrypoint for the current
	/// host's runtime identifier under the canonical <c>runtimes/&lt;rid&gt;/</c> layout - clean at every
	/// level except the six publication fields, and never a layout or payload defect of its own.</summary>
	private static string PublicationIncompleteDirectory()
	{
		var directory = Directory.CreateTempSubdirectory("macrodeck-plugin-cli-tests-").FullName;
		var rid = PluginRuntimeIdentifiers.Current;
		WriteRuntimeEntrypoint(directory, rid, create: true);

		var json = $$"""
					 {
					 	"manifestVersion": 1,
					 	"id": "{{ManifestFixtures.PluginId}}",
					 	"name": "Test Plugin",
					 	"version": "{{ManifestFixtures.Version}}",
					 	"entrypoints": {
					 		"{{rid}}": { "executable": "{{RuntimeEntrypointPath(rid)}}" }
					 	}
					 }
					 """;
		File.WriteAllText(Path.Combine(directory, "manifest.json"), json);
		return directory;
	}

	/// <summary>As <see cref="PublicationIncompleteDirectory" />, but complete on every publication field
	/// except <c>license</c> - the one deliberate gap.</summary>
	private static string AlmostPublicationCompleteDirectory(out string rid)
	{
		var directory = Directory.CreateTempSubdirectory("macrodeck-plugin-cli-tests-").FullName;
		rid = PluginRuntimeIdentifiers.Current;
		WriteRuntimeEntrypoint(directory, rid, create: true);

		Directory.CreateDirectory(Path.Combine(directory, "assets"));
		File.WriteAllText(Path.Combine(directory, "assets", "icon.svg"), "icon");

		var json = $$"""
					 {
					 	"manifestVersion": 1,
					 	"id": "{{ManifestFixtures.PluginId}}",
					 	"name": "Test Plugin",
					 	"version": "{{ManifestFixtures.Version}}",
					 	"description": "A plugin.",
					 	"icon": "assets/icon.svg",
					 	"publisher": { "name": "Example Publisher" },
					 	"repository": "https://example.com/repo",
					 	"compatibility": { "macroDeck": ">=3.0.0" },
					 	"entrypoints": {
					 		"{{rid}}": { "executable": "{{RuntimeEntrypointPath(rid)}}" }
					 	}
					 }
					 """;
		File.WriteAllText(Path.Combine(directory, "manifest.json"), json);
		return directory;
	}

	private static string RuntimeEntrypointPath(string rid) =>
		rid.StartsWith("win-", StringComparison.Ordinal) ? $"runtimes/{rid}/App.exe" : $"runtimes/{rid}/App";

	private static void WriteRuntimeEntrypoint(string directory, string rid, bool create)
	{
		if (!create)
		{
			return;
		}

		var path = Path.Combine(directory, RuntimeEntrypointPath(rid).Replace('/', Path.DirectorySeparatorChar));
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, "binary");
	}
}
