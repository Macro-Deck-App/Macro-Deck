using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Plugin.Cli.Building;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>
/// <c>build</c> as a function: a plugin project mapped to a <see cref="PluginBuildResult" />, with the
/// build toolchain - the only real external boundary - replaced by <see cref="FakePluginBuildRunner" />.
/// Staging, manifest rewriting and packing all run for real against real directories, and every assertion
/// about the produced package reads the artifact back rather than trusting the builder's own report.
/// </summary>
[TestFixture]
public class PluginBuilderTests
{
	private static readonly string[] _iconEntry = ["assets/icon.png"];

	private static readonly string[] _transientPrefixes = ["bin/", "obj/", ".git/", "node_modules/"];

	private string _output = null!;
	private string _stagingRoot = null!;

	[SetUp]
	public void SetUp()
	{
		_output = Directory.CreateTempSubdirectory("macrodeck-plugin-build-out-").FullName;
		_stagingRoot = Directory.CreateTempSubdirectory("macrodeck-plugin-build-staging-").FullName;
	}

	[TearDown]
	public void TearDown()
	{
		Delete(_output);
		Delete(_stagingRoot);
	}

	[Test]
	public async Task A_full_build_runs_every_declared_target_once_and_packs_them_into_one_artifact()
	{
		var rids = ManifestFixtures.PickForeignRids(3);
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			var runner = new FakePluginBuildRunner { OnRun = BuildFixtures.ProducingOutput(project, rids) };

			var result = await BuildAsync(project, runner);

			var entries = ArtifactEntries(result.Pack!.OutputPath!);

			Assert.Multiple(() =>
			{
				Assert.That(result.Success, Is.True, result.FailureMessage);
				Assert.That(runner.Invocations, Has.Count.EqualTo(rids.Count));
				Assert.That(runner.Invocations.Select(invocation => invocation.Arguments[1]),
					Is.EquivalentTo(rids));

				foreach (var rid in rids)
				{
					// Each runtime identifier's payload sits under its own declared directory - the whole point
					// of the layout, since two of these produce identically named executables.
					Assert.That(entries, Does.Contain(BuildFixtures.EntrypointPath(rid)));
					Assert.That(entries, Does.Contain($"runtimes/{rid}/{BuildFixtures.ProjectName}.deps.json"));
				}

				// The packer's own entrypoint-presence check, which pack only warns about, finds nothing to
				// warn about after a complete build.
				Assert.That(result.Pack.Warnings.Select(warning => warning.Code),
					Does.Not.Contain("entrypoint-not-packed"));
			});
		}
		finally
		{
			Delete(project);
		}
	}

	[Test]
	public async Task Shared_assets_are_packaged_once_at_the_package_root_however_many_targets_are_built()
	{
		var rids = ManifestFixtures.PickForeignRids(3);
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			var runner = new FakePluginBuildRunner { OnRun = BuildFixtures.ProducingOutput(project, rids) };

			var result = await BuildAsync(project, runner);
			var entries = ArtifactEntries(result.Pack!.OutputPath!);

			Assert.Multiple(() =>
			{
				// Exactly once, and at the path the manifest's own "icon" refers to - a per-RID copy would
				// still contain the name but would neither resolve nor belong in the package three times.
				Assert.That(entries.Count(entry => entry == "assets/icon.png"), Is.EqualTo(1));
				Assert.That(entries.Count(entry => entry == "README.md"), Is.EqualTo(1));
				Assert.That(entries.Where(entry => entry.EndsWith("icon.png", StringComparison.Ordinal)),
					Is.EquivalentTo(_iconEntry));
			});
		}
		finally
		{
			Delete(project);
		}
	}

	[Test]
	public async Task The_build_recipe_and_transient_directories_are_never_packaged()
	{
		var rids = ManifestFixtures.PickForeignRids(1);
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			Directory.CreateDirectory(Path.Combine(project, ".git"));
			await File.WriteAllTextAsync(Path.Combine(project, ".git", "config"), "[core]");
			Directory.CreateDirectory(Path.Combine(project, "node_modules", "pkg"));
			await File.WriteAllTextAsync(Path.Combine(project, "node_modules", "pkg", "index.js"), "module");

			var runner = new FakePluginBuildRunner { OnRun = BuildFixtures.ProducingOutput(project, rids) };

			var result = await BuildAsync(project, runner);
			var entries = ArtifactEntries(result.Pack!.OutputPath!);

			Assert.Multiple(() =>
			{
				Assert.That(entries, Does.Not.Contain("macrodeck-build.json"));

				foreach (var prefix in _transientPrefixes)
				{
					Assert.That(entries.Where(entry => entry.StartsWith(prefix, StringComparison.Ordinal)),
						Is.Empty,
						$"'{prefix}' must never be packaged.");
				}

				// The exclusion is targeted, not "drop everything that is not build output".
				Assert.That(entries, Does.Contain("assets/icon.png"));
			});
		}
		finally
		{
			Delete(project);
		}
	}

	[Test]
	public async Task A_stale_runtimes_directory_in_the_project_never_overwrites_freshly_built_output()
	{
		var rids = ManifestFixtures.PickForeignRids(1);
		var rid = rids[0];
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			// A previous manual publish left behind in the project tree. Whatever this build produces must win.
			Directory.CreateDirectory(Path.Combine(project, "runtimes", rid));
			await File.WriteAllTextAsync(Path.Combine(project, "runtimes", rid, BuildFixtures.ExecutableName(rid)),
				"stale");

			var runner = new FakePluginBuildRunner
			{
				OnRun = (_, _, _) => BuildFixtures.WriteBuildOutput(project, rid, "freshly built")
			};

			var result = await BuildAsync(project, runner);

			Assert.That(result.Success, Is.True, result.FailureMessage);
			Assert.That(ReadArtifactEntry(result.Pack!.OutputPath!, BuildFixtures.EntrypointPath(rid)),
				Is.EqualTo("freshly built"));
		}
		finally
		{
			Delete(project);
		}
	}

	[Test]
	public async Task A_declared_entrypoint_the_build_did_not_produce_fails_the_command()
	{
		var rids = ManifestFixtures.PickForeignRids(1);
		var rid = rids[0];
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			// The trap the requirement names: the tool succeeds, but produces a .dll where the manifest
			// declares a self-contained executable.
			var runner = new FakePluginBuildRunner
			{
				OnRun = (_, _, _) =>
				{
					var output = Path.Combine(project, BuildFixtures.OutputDirectory(rid));
					Directory.CreateDirectory(output);
					File.WriteAllText(Path.Combine(output, $"{BuildFixtures.ProjectName}.dll"), "wrong shape");
				}
			};

			var result = await BuildAsync(project, runner);

			Assert.Multiple(() =>
			{
				Assert.That(result.Success, Is.False);
				Assert.That(result.FailureReason, Is.EqualTo(PluginBuildFailureReason.EntrypointMissing));
				Assert.That(PluginBuildFailureExitCode.For(result.FailureReason),
					Is.EqualTo(ExitCode.SubjectInvalid));
				Assert.That(result.FailureDetail, Does.Contain(rid).And.Contain(BuildFixtures.ExecutableName(rid)));

				// Where pack warns and still writes an artifact, build writes none.
				Assert.That(Directory.EnumerateFiles(_output), Is.Empty);
			});
		}
		finally
		{
			Delete(project);
		}
	}

	[Test]
	public async Task Building_one_runtime_identifier_does_not_fail_because_the_others_were_not_built()
	{
		var rids = ManifestFixtures.PickForeignRids(3);
		var selected = rids[1];
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			var runner = new FakePluginBuildRunner
			{
				OnRun = (_, _, _) => BuildFixtures.WriteBuildOutput(project, selected)
			};

			var result = await BuildAsync(project, runner, rid: selected);

			var manifest = ReadPackagedManifest(result.Pack!.OutputPath!);

			Assert.Multiple(() =>
			{
				Assert.That(result.Success, Is.True, result.FailureMessage);
				Assert.That(runner.Invocations, Has.Count.EqualTo(1));

				// The artifact declares only what it actually contains, so it is a valid single-platform
				// package rather than one claiming platforms this invocation never produced.
				Assert.That(manifest.RootElement.GetProperty("entrypoints").EnumerateObject().Select(p => p.Name),
					Is.EquivalentTo(new[] { selected }));
				Assert.That(Path.GetFileName(result.Pack.OutputPath!),
					Is.EqualTo($"{BuildFixtures.PluginId}-{BuildFixtures.Version}-{selected}.macroDeckPlugin"));
			});
		}
		finally
		{
			Delete(project);
		}
	}

	[Test]
	public async Task A_runtime_identifier_the_manifest_does_not_declare_is_refused_before_anything_is_built()
	{
		var rids = ManifestFixtures.PickForeignRids(2);
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			var runner = new FakePluginBuildRunner();

			var result = await BuildAsync(project, runner, rid: "totally-made-up-rid");

			Assert.Multiple(() =>
			{
				Assert.That(result.FailureReason, Is.EqualTo(PluginBuildFailureReason.RidNotDeclared));
				Assert.That(PluginBuildFailureExitCode.For(result.FailureReason), Is.EqualTo(ExitCode.UsageError));
				Assert.That(result.FailureMessage, Does.Contain("totally-made-up-rid").And.Contain(rids[0]));
				Assert.That(runner.Invocations, Is.Empty);
			});
		}
		finally
		{
			Delete(project);
		}
	}

	[Test]
	public async Task A_declared_runtime_identifier_with_no_build_recipe_fails_rather_than_being_skipped()
	{
		var rids = ManifestFixtures.PickForeignRids(2);
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			// The build config knows about the first runtime identifier only, while the manifest still claims
			// both. A full build must never quietly ship the half it can build.
			await File.WriteAllTextAsync(Path.Combine(project, "macrodeck-build.json"),
				BuildFixtures.BuildConfigJson([rids[0]]));

			var runner = new FakePluginBuildRunner { OnRun = BuildFixtures.ProducingOutput(project, rids) };

			var result = await BuildAsync(project, runner);

			Assert.Multiple(() =>
			{
				Assert.That(result.FailureReason, Is.EqualTo(PluginBuildFailureReason.TargetNotConfigured));
				Assert.That(result.FailureMessage, Does.Contain(rids[1]));
				Assert.That(Directory.EnumerateFiles(_output), Is.Empty);
			});
		}
		finally
		{
			Delete(project);
		}
	}

	[Test]
	public async Task A_build_tool_that_fails_reports_its_runtime_identifier_and_both_of_its_output_streams()
	{
		var rids = ManifestFixtures.PickForeignRids(1);
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			var runner = new FakePluginBuildRunner
			{
				Result = new PluginBuildRunResult(3, "error CS1002: ; expected", "MSBuild reported an error")
			};

			var result = await BuildAsync(project, runner);

			Assert.Multiple(() =>
			{
				Assert.That(result.FailureReason, Is.EqualTo(PluginBuildFailureReason.BuildFailed));
				Assert.That(PluginBuildFailureExitCode.For(result.FailureReason),
					Is.EqualTo(ExitCode.SubjectInvalid));
				Assert.That(result.FailureMessage, Does.Contain(rids[0]));

				// Neither stream alone is enough: a tool's real failure text lands on either one.
				Assert.That(result.FailureDetail,
					Does.Contain("error CS1002").And.Contain("MSBuild reported an error"));
			});
		}
		finally
		{
			Delete(project);
		}
	}

	[Test]
	public async Task A_build_tool_that_cannot_be_launched_names_the_runtime_identifier_and_the_executable()
	{
		var rids = ManifestFixtures.PickForeignRids(1);
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			var runner = new FakePluginBuildRunner
			{
				ThrowOn = FakePluginBuildRunner.NotLaunchable("fixture-build-tool")
			};

			var result = await BuildAsync(project, runner);

			Assert.Multiple(() =>
			{
				Assert.That(result.FailureReason, Is.EqualTo(PluginBuildFailureReason.BuildToolNotFound));
				Assert.That(PluginBuildFailureExitCode.For(result.FailureReason),
					Is.EqualTo(ExitCode.InputUnreadable));
				Assert.That(result.FailureMessage, Does.Contain(rids[0]).And.Contain("fixture-build-tool"));
			});
		}
		finally
		{
			Delete(project);
		}
	}

	[Test]
	public async Task Staging_leaves_nothing_behind_on_success_or_on_failure()
	{
		var rids = ManifestFixtures.PickForeignRids(1);
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			var succeeding = new FakePluginBuildRunner { OnRun = BuildFixtures.ProducingOutput(project, rids) };
			var success = await BuildAsync(project, succeeding);

			Assert.That(success.Success, Is.True, success.FailureMessage);
			Assert.That(Directory.EnumerateFileSystemEntries(_stagingRoot), Is.Empty);

			var failing = new FakePluginBuildRunner { Result = new PluginBuildRunResult(1, string.Empty, "boom") };
			var failure = await BuildAsync(project, failing, force: true);

			Assert.That(failure.Success, Is.False);
			Assert.That(Directory.EnumerateFileSystemEntries(_stagingRoot), Is.Empty);
		}
		finally
		{
			Delete(project);
		}
	}

	[Test]
	public async Task An_existing_artifact_is_refused_before_the_build_runs_and_replaced_with_force()
	{
		var rids = ManifestFixtures.PickForeignRids(1);
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			var artifact = Path.Combine(_output,
				$"{BuildFixtures.PluginId}-{BuildFixtures.Version}.macroDeckPlugin");
			await File.WriteAllTextAsync(artifact, "previous artifact");

			var refused = new FakePluginBuildRunner { OnRun = BuildFixtures.ProducingOutput(project, rids) };
			var refusal = await BuildAsync(project, refused);

			Assert.Multiple(() =>
			{
				Assert.That(refusal.FailureReason, Is.EqualTo(PluginBuildFailureReason.OutputExists));
				Assert.That(PluginBuildFailureExitCode.For(refusal.FailureReason), Is.EqualTo(ExitCode.UsageError));
				Assert.That(File.ReadAllText(artifact), Is.EqualTo("previous artifact"));

				// Refused before spending the build, not after.
				Assert.That(refused.Invocations, Is.Empty);
			});

			var forced = new FakePluginBuildRunner { OnRun = BuildFixtures.ProducingOutput(project, rids) };
			var forcedResult = await BuildAsync(project, forced, force: true);

			Assert.That(forcedResult.Success, Is.True, forcedResult.FailureMessage);
			Assert.That(ArtifactEntries(artifact), Does.Contain(BuildFixtures.EntrypointPath(rids[0])));
		}
		finally
		{
			Delete(project);
		}
	}

	[Test]
	public async Task A_manifest_that_could_never_pack_is_refused_before_any_target_is_built()
	{
		var rids = ManifestFixtures.PickForeignRids(2);
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			// An id that is not a reverse-domain name violates the published schema. Discovering that after a
			// multi-platform publish would cost minutes of CI for a manifest that can never become an artifact.
			await File.WriteAllTextAsync(Path.Combine(project, "manifest.json"),
				BuildFixtures.ManifestJson(rids)
					.Replace($"\"{BuildFixtures.PluginId}\"", "\"Not A Valid Plugin Id\"", StringComparison.Ordinal));

			var runner = new FakePluginBuildRunner { OnRun = BuildFixtures.ProducingOutput(project, rids) };

			var result = await BuildAsync(project, runner);

			Assert.Multiple(() =>
			{
				Assert.That(result.FailureReason, Is.EqualTo(PluginBuildFailureReason.ManifestInvalid));
				Assert.That(PluginBuildFailureExitCode.For(result.FailureReason),
					Is.EqualTo(ExitCode.SubjectInvalid));
				Assert.That(runner.Invocations, Is.Empty);
			});
		}
		finally
		{
			Delete(project);
		}
	}

	[Test]
	public async Task A_stale_files_list_and_signature_neither_fail_the_build_nor_reach_the_artifact()
	{
		var rids = ManifestFixtures.PickForeignRids(1);
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			// A manifest that was signed and packed once already. Its files[] describes bytes that no longer
			// exist, and its signature covers a digest this build is about to invalidate.
			var stale = JsonNode.Parse(BuildFixtures.ManifestJson(rids))!.AsObject();
			stale["files"] = new JsonArray(new JsonObject
			{
				["path"] = "gone.dll",
				["sha256"] = $"sha256:{new string('a', 64)}",
				["size"] = 1
			});
			stale["signature"] = new JsonObject
			{
				["algorithm"] = "ed25519",
				["keyId"] = "fixture-key",
				["value"] = "AA=="
			};

			await File.WriteAllTextAsync(Path.Combine(project, "manifest.json"), stale.ToJsonString());

			var runner = new FakePluginBuildRunner { OnRun = BuildFixtures.ProducingOutput(project, rids) };

			var result = await BuildAsync(project, runner);
			var manifest = ReadPackagedManifest(result.Pack!.OutputPath!);

			Assert.Multiple(() =>
			{
				Assert.That(result.Success, Is.True, result.FailureMessage);

				// build produces an unsigned package; carrying the old signature over would claim a digest
				// that no longer exists.
				Assert.That(manifest.RootElement.TryGetProperty("signature", out _), Is.False);

				// files[] is the packer's, recomputed from the staged bytes.
				Assert.That(manifest.RootElement.GetProperty("files")
						.EnumerateArray()
						.Select(file => file.GetProperty("path").GetString()),
					Does.Not.Contain("gone.dll"));
			});
		}
		finally
		{
			Delete(project);
		}
	}

	[Test]
	public async Task The_packaged_manifest_declares_the_languages_the_project_has_resources_for()
	{
		var rids = ManifestFixtures.PickForeignRids(1);
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			var resources = Path.Combine(project, "Localization");
			Directory.CreateDirectory(resources);

			foreach (var fileName in (string[])["Strings.resx", "Strings.de.resx", "Strings.pt-BR.resx"])
			{
				await File.WriteAllTextAsync(Path.Combine(resources, fileName),
					"""<?xml version="1.0" encoding="utf-8"?><root />""");
			}

			var runner = new FakePluginBuildRunner { OnRun = BuildFixtures.ProducingOutput(project, rids) };

			var result = await BuildAsync(project, runner);
			var manifest = ReadPackagedManifest(result.Pack!.OutputPath!);

			Assert.Multiple(() =>
			{
				Assert.That(result.Success, Is.True, result.FailureMessage);

				// 'pt-BR' whole, not truncated: Brazilian and European Portuguese are different languages
				// to a reader, and both would collapse onto 'pt'.
				Assert.That(manifest.RootElement.GetProperty("languages")
						.EnumerateArray()
						.Select(language => language.GetString()),
					Is.EqualTo((string[])["de", "en", "pt-BR"]));
			});
		}
		finally
		{
			Delete(project);
		}
	}

	[Test]
	public async Task Two_runtime_identifiers_sharing_a_staging_directory_are_refused_before_anything_is_built()
	{
		var rids = ManifestFixtures.PickForeignRids(2);
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			// Both entrypoints in one directory: whichever built last would win, so the package contents
			// would depend on build order.
			await File.WriteAllTextAsync(Path.Combine(project, "manifest.json"),
				BuildFixtures.ManifestJson(rids)
					.Replace($"runtimes/{rids[1]}/", $"runtimes/{rids[0]}/"));

			var runner = new FakePluginBuildRunner { OnRun = BuildFixtures.ProducingOutput(project, rids) };

			var result = await BuildAsync(project, runner);

			Assert.Multiple(() =>
			{
				Assert.That(result.FailureReason, Is.EqualTo(PluginBuildFailureReason.EntrypointLayoutInvalid));
				Assert.That(result.FailureMessage, Does.Contain(rids[0]).And.Contain(rids[1]));
				Assert.That(runner.Invocations, Is.Empty);
			});
		}
		finally
		{
			Delete(project);
		}
	}

	[Test]
	public async Task Build_arguments_reach_the_tool_as_a_vector_rather_than_a_command_string()
	{
		var rids = ManifestFixtures.PickForeignRids(1);
		var rid = rids[0];
		var project = BuildFixtures.WriteProject(rids);

		try
		{
			// A deliberately non-.NET recipe carrying an argument that a shell would mangle.
			await File.WriteAllTextAsync(Path.Combine(project, "macrodeck-build.json"),
				$$"""
				  {
				    "version": 1,
				    "targets": {
				      "{{rid}}": {
				        "executable": "my-builder",
				        "arguments": ["--out", "bin/publish/{{rid}}", "--label", "a b && rm -rf .", "$HOME"],
				        "output": "bin/publish/{{rid}}"
				      }
				    }
				  }
				  """);

			var runner = new FakePluginBuildRunner
			{
				OnRun = (_, _, _) => BuildFixtures.WriteBuildOutput(project, rid)
			};

			var result = await BuildAsync(project, runner);

			Assert.Multiple(() =>
			{
				Assert.That(result.Success, Is.True, result.FailureMessage);
				Assert.That(runner.Invocations[0].Executable, Is.EqualTo("my-builder"));

				// Element-wise: a shell-joined command string would collapse these into one, expand $HOME, or
				// split the label on its spaces.
				Assert.That(runner.Invocations[0].Arguments,
					Is.EqualTo(new[] { "--out", $"bin/publish/{rid}", "--label", "a b && rm -rf .", "$HOME" }));
			});
		}
		finally
		{
			Delete(project);
		}
	}

	private Task<PluginBuildResult> BuildAsync(string project,
		IPluginBuildRunner runner,
		string? rid = null,
		bool force = false)
	{
		return PluginBuilder.BuildAsync(new PluginBuildRequest
			{
				SourceDirectory = project,
				ManifestPath = Path.Combine(project, "manifest.json"),
				BuildConfigPath = Path.Combine(project, "macrodeck-build.json"),
				Rid = rid,
				OutputDirectory = _output,
				Force = force,
				StagingRoot = _stagingRoot
			},
			runner);
	}

	private static List<string> ArtifactEntries(string artifactPath)
	{
		using var archive = ZipFile.OpenRead(artifactPath);

		return archive.Entries.Select(entry => entry.FullName).ToList();
	}

	private static string ReadArtifactEntry(string artifactPath, string entryName)
	{
		using var archive = ZipFile.OpenRead(artifactPath);
		using var reader = new StreamReader(archive.GetEntry(entryName)!.Open());

		return reader.ReadToEnd();
	}

	private static JsonDocument ReadPackagedManifest(string artifactPath)
		=> JsonDocument.Parse(ReadArtifactEntry(artifactPath, "manifest.json"));

	private static void Delete(string directory)
	{
		if (Directory.Exists(directory))
		{
			Directory.Delete(directory, recursive: true);
		}
	}
}
