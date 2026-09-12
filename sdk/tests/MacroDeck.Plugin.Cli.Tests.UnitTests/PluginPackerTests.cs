using System.IO.Compression;
using System.Security.Cryptography;
using MacroDeck.Plugin.Cli.Packing;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary><c>pack</c> as a function: <see cref="PluginPacker.PackAsync" /> mapped from a source tree to
/// a <see cref="PluginPackResult" />, with no process ever spawned.</summary>
[TestFixture]
public class PluginPackerTests
{
	[Test]
	public async Task PackAsync_refuses_a_source_tree_containing_an_entry_the_artifact_policy_rejects()
	{
		var directory = ManifestFixtures.WriteValidManifestDirectory();
		var outputPath = Path.Combine(Path.GetTempPath(),
			$"macrodeck-plugin-cli-tests-{Guid.NewGuid():N}.macroDeckPlugin");

		try
		{
			// CON is one of the reserved Windows device names PluginArtifactEntryPolicy rejects
			// (docs/src/content/docs/guides/hosting.md's installer rejection table) - a perfectly legal
			// file name to create on this test machine, which is exactly why the policy has to catch it
			// before packing, not rely on the file system to refuse it.
			await File.WriteAllTextAsync(Path.Combine(directory, "CON"), "not a device file");

			var result = await PluginPacker.PackAsync(directory,
				Path.Combine(directory, "manifest.json"),
				_ => outputPath,
				force: false);

			Assert.Multiple(() =>
			{
				Assert.That(result.Success, Is.False);
				Assert.That(result.FailureReason, Is.EqualTo(PluginPackFailureReason.SourceEntryRejected));
				Assert.That(result.FailureMessage, Does.Contain("CON"));
				Assert.That(PluginPackFailureExitCode.For(result.FailureReason), Is.EqualTo(ExitCode.SubjectInvalid));

				// No artifact left behind for a rejected pack.
				Assert.That(File.Exists(outputPath), Is.False);
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);

			if (File.Exists(outputPath))
			{
				File.Delete(outputPath);
			}
		}
	}

	[Test]
	public async Task PackAsync_into_the_source_tree_never_packs_its_own_output_or_an_earlier_artifact()
	{
		var directory = ManifestFixtures.WriteValidManifestDirectory();
		var outputPath = Path.Combine(directory, "plugin.macroDeckPlugin");
		var renamedOutputPath = Path.Combine(directory, "out", "plugin.zip");

		try
		{
			var first = await PluginPacker.PackAsync(directory,
				Path.Combine(directory, "manifest.json"),
				_ => outputPath,
				force: false);
			var second = await PluginPacker.PackAsync(directory,
				Path.Combine(directory, "manifest.json"),
				_ => renamedOutputPath,
				force: false);
			var third = await PluginPacker.PackAsync(directory,
				Path.Combine(directory, "manifest.json"),
				_ => renamedOutputPath,
				force: true);

			Assert.That(first.Success && second.Success && third.Success, Is.True);

			using var archive = ZipFile.OpenRead(renamedOutputPath);
			var entries = archive.Entries.Select(entry => entry.FullName).ToList();

			Assert.Multiple(() =>
			{
				Assert.That(entries, Does.Not.Contain("plugin.macroDeckPlugin"));
				Assert.That(entries, Does.Not.Contain("out/plugin.zip"));
				Assert.That(entries, Does.Contain(ManifestFixtures.EntrypointFileName));
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public async Task PackAsync_output_inspects_clean_through_the_independent_reader_and_its_file_digests_verify()
	{
		var directory = ManifestFixtures.WriteValidManifestDirectory();
		var payload = "hello from a fixture payload, not from PluginPacker itself"u8.ToArray();
		await File.WriteAllBytesAsync(Path.Combine(directory, ManifestFixtures.EntrypointFileName), payload);

		var outputPath = Path.Combine(Path.GetTempPath(),
			$"macrodeck-plugin-cli-tests-{Guid.NewGuid():N}.macroDeckPlugin");
		string? extractDirectory = null;

		try
		{
			var packResult = await PluginPacker.PackAsync(directory,
				Path.Combine(directory, "manifest.json"),
				_ => outputPath,
				force: false);

			Assert.That(packResult.Success, Is.True, packResult.FailureMessage);

			// Genuinely independent of PluginPacker: the same reader the host and every other command use.
			var reader = new PluginArtifactReader(new PluginManifestReader(),
				Serilog.Core.Logger.None);

			var inspection = await reader.Inspect(outputPath);
			Assert.That(inspection.Success, Is.True, inspection.ErrorMessage);

			var manifest = inspection.Manifest!;
			Assert.That(manifest.Files, Is.Not.Null.And.Not.Empty);

			extractDirectory = Directory.CreateTempSubdirectory("macrodeck-plugin-cli-tests-extract-").FullName;
			var extraction = await reader.ExtractTo(outputPath, extractDirectory);
			Assert.That(extraction.Success, Is.True, extraction.ErrorMessage);

			Assert.Multiple(() =>
			{
				foreach (var file in manifest.Files!)
				{
					var extractedPath = Path.Combine(extractDirectory,
						file.Path.Replace('/', Path.DirectorySeparatorChar));
					Assert.That(File.Exists(extractedPath), Is.True, $"'{file.Path}' was not extracted.");

					var actualBytes = File.ReadAllBytes(extractedPath);
					var actualSha256 = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(actualBytes));

					Assert.That(actualSha256, Is.EqualTo(file.Sha256), $"'{file.Path}' digest mismatch.");
					Assert.That(actualBytes.Length, Is.EqualTo(file.Size), $"'{file.Path}' size mismatch.");
				}
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);

			if (extractDirectory is not null)
			{
				Directory.Delete(extractDirectory, recursive: true);
			}

			if (File.Exists(outputPath))
			{
				File.Delete(outputPath);
			}
		}
	}

	[Test]
	public async Task Packing_a_self_contained_payload_whose_recomputed_manifest_exceeds_64_KiB_succeeds()
	{
		var foreignRids = ManifestFixtures.PickForeignRids(2);
		var directory = ManifestFixtures.WriteMultiRidManifestDirectory(foreignRids, createForeignFiles: true);
		var outputPath = Path.Combine(Path.GetTempPath(),
			$"macrodeck-plugin-cli-tests-{Guid.NewGuid():N}.macroDeckPlugin");

		try
		{
			// Shaped like what `build` stages for a multi-RID self-contained publish: one runtimes/<rid>
			// subtree per RID, each carrying the runtime's own files. Only the file count and the path
			// lengths matter here, so every file is one byte.
			var payloadFileCount = 0;
			foreach (var rid in foreignRids.Append(PluginRuntimeIdentifiers.Current))
			{
				var runtimeDirectory = Path.Combine(directory, "runtimes", rid);
				Directory.CreateDirectory(runtimeDirectory);

				for (var index = 0; index < 250; index++)
				{
					await File.WriteAllBytesAsync(
						Path.Combine(runtimeDirectory, $"System.Runtime.Fixture.Part{index:D4}.dll"),
						[0x00]);
					payloadFileCount++;
				}
			}

			var result = await PluginPacker.PackAsync(directory,
				Path.Combine(directory, "manifest.json"),
				_ => outputPath,
				force: false);

			Assert.That(result.Success, Is.True, result.FailureMessage);

			using var archive = ZipFile.OpenRead(outputPath);
			var manifestEntry = archive.GetEntry(PluginArtifactFiles.ManifestFileName);
			Assert.That(manifestEntry, Is.Not.Null);

			// The manifest this payload produces is exactly what issue #751 reports as unpackable: a
			// files[] section past the old 64 KiB cap. It has to pack, and it has to read back through
			// the reader the host installs with - a limit raised on only one of the two sides would
			// produce an artifact nothing can install.
			Assert.That(manifestEntry!.Length, Is.GreaterThan(64 * 1024));

			var inspection = await new PluginArtifactReader(new PluginManifestReader(), Serilog.Core.Logger.None)
				.Inspect(outputPath);

			Assert.Multiple(() =>
			{
				Assert.That(inspection.Success, Is.True, inspection.ErrorMessage);
				Assert.That(inspection.Manifest!.Files!,
					Has.Count.EqualTo(payloadFileCount + 1 + foreignRids.Count));
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);

			if (File.Exists(outputPath))
			{
				File.Delete(outputPath);
			}
		}
	}

	[Test]
	public async Task Packing_warns_for_an_entrypoint_absent_from_the_source_tree_and_still_succeeds()
	{
		var foreignRids = ManifestFixtures.PickForeignRids(2);
		var directory = ManifestFixtures.WriteMultiRidManifestDirectory(foreignRids, createForeignFiles: false);
		var outputPath = Path.Combine(Path.GetTempPath(),
			$"macrodeck-plugin-cli-tests-{Guid.NewGuid():N}.macroDeckPlugin");

		try
		{
			var result = await PluginPacker.PackAsync(directory,
				Path.Combine(directory, "manifest.json"),
				_ => outputPath,
				force: false);

			var output = new StringWriter();
			var error = new StringWriter();
			var console = new CliConsole(Verbosity.Normal, noColor: true, output, error);
			var exitCode = await PluginPackReporter.ReportAsync(console, result, showDigest: false);

			Assert.Multiple(() =>
			{
				Assert.That(result.Success, Is.True, result.FailureMessage);
				Assert.That(File.Exists(outputPath), Is.True);
				Assert.That(exitCode, Is.EqualTo(ExitCode.Success));

				// One warning per RID whose declared file never showed up in the source tree, each naming its
				// own RID - never a single generic "something is missing" line. The fixture also declares no
				// publisher/description/etc., so publication-readiness warnings are expected alongside these
				// and are not this assertion's concern.
				var entrypointWarnings = result.Warnings.Where(w => w.Code == "entrypoint-not-packed").ToList();
				Assert.That(entrypointWarnings, Has.Count.EqualTo(foreignRids.Count));

				foreach (var rid in foreignRids)
				{
					Assert.That(entrypointWarnings, Has.Some.Matches<CliDiagnostic>(w => w.Message.Contains(rid)));
				}
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);

			if (File.Exists(outputPath))
			{
				File.Delete(outputPath);
			}
		}
	}

	[Test]
	public async Task Packing_a_complete_multi_platform_build_warns_about_no_missing_entrypoint()
	{
		var foreignRids = ManifestFixtures.PickForeignRids(2);
		var directory = ManifestFixtures.WriteMultiRidManifestDirectory(foreignRids, createForeignFiles: true);
		var outputPath = Path.Combine(Path.GetTempPath(),
			$"macrodeck-plugin-cli-tests-{Guid.NewGuid():N}.macroDeckPlugin");

		try
		{
			var result = await PluginPacker.PackAsync(directory,
				Path.Combine(directory, "manifest.json"),
				_ => outputPath,
				force: false);

			Assert.Multiple(() =>
			{
				Assert.That(result.Success, Is.True, result.FailureMessage);

				// No entrypoint warning - every declared RID's file is present. The fixture declares no
				// publisher/description/etc., so publication-readiness warnings are still expected and are
				// not this assertion's concern.
				Assert.That(result.Warnings, Has.None.Matches<CliDiagnostic>(w => w.Code == "entrypoint-not-packed"));
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);

			if (File.Exists(outputPath))
			{
				File.Delete(outputPath);
			}
		}
	}

	[Test]
	public async Task Packing_reports_an_output_directory_it_had_to_create()
	{
		var directory = ManifestFixtures.WriteValidManifestDirectory();
		var nestedRoot = Path.Combine(Path.GetTempPath(), $"macrodeck-plugin-cli-tests-out-{Guid.NewGuid():N}");
		var outputPath = Path.Combine(nestedRoot, "nested", "plugin.macroDeckPlugin");

		try
		{
			var firstPack = await PluginPacker.PackAsync(directory,
				Path.Combine(directory, "manifest.json"),
				_ => outputPath,
				force: false);

			Assert.That(firstPack.Success, Is.True, firstPack.FailureMessage);
			Assert.That(firstPack.CreatedOutputDirectory,
				Is.Not.Null,
				"The output directory did not exist before this pack - it should be reported as created.");

			var secondPack = await PluginPacker.PackAsync(directory,
				Path.Combine(directory, "manifest.json"),
				_ => outputPath,
				force: true);

			Assert.That(secondPack.Success, Is.True, secondPack.FailureMessage);
			Assert.That(secondPack.CreatedOutputDirectory,
				Is.Null,
				"The directory already existed by the second pack - nothing was created this time.");
		}
		finally
		{
			Directory.Delete(directory, recursive: true);

			if (Directory.Exists(nestedRoot))
			{
				Directory.Delete(nestedRoot, recursive: true);
			}
		}
	}

	[Test]
	public async Task Packing_a_bin_Debug_source_is_noted_and_a_bin_Release_source_is_not()
	{
		var debugRoot = Path.Combine(Path.GetTempPath(),
			$"macrodeck-plugin-cli-tests-{Guid.NewGuid():N}",
			"bin",
			"Debug",
			"net10.0");

		// The counterexample the test name promises: a directory segment that merely contains "Release" as
		// a substring of an unrelated name, never "bin/Release" itself.
		var releaseRoot = Path.Combine(Path.GetTempPath(),
			$"macrodeck-plugin-cli-tests-{Guid.NewGuid():N}",
			"MyDebugger",
			"bin",
			"Release");

		Directory.CreateDirectory(debugRoot);
		Directory.CreateDirectory(releaseRoot);

		try
		{
			WriteManifestInto(debugRoot);
			WriteManifestInto(releaseRoot);

			var debugOutput = Path.Combine(Path.GetTempPath(),
				$"macrodeck-plugin-cli-tests-{Guid.NewGuid():N}.macroDeckPlugin");
			var releaseOutput = Path.Combine(Path.GetTempPath(),
				$"macrodeck-plugin-cli-tests-{Guid.NewGuid():N}.macroDeckPlugin");

			try
			{
				var debugResult = await PluginPacker.PackAsync(debugRoot,
					Path.Combine(debugRoot, "manifest.json"),
					_ => debugOutput,
					force: false);

				var releaseResult = await PluginPacker.PackAsync(releaseRoot,
					Path.Combine(releaseRoot, "manifest.json"),
					_ => releaseOutput,
					force: false);

				Assert.Multiple(() =>
				{
					Assert.That(debugResult.Success, Is.True, debugResult.FailureMessage);
					Assert.That(debugResult.Warnings,
						Has.Some.Matches<CliDiagnostic>(w => w.Code == "source-looks-like-debug-build"));

					Assert.That(releaseResult.Success, Is.True, releaseResult.FailureMessage);
					Assert.That(releaseResult.Warnings,
						Has.None.Matches<CliDiagnostic>(w => w.Code == "source-looks-like-debug-build"));
				});
			}
			finally
			{
				if (File.Exists(debugOutput))
				{
					File.Delete(debugOutput);
				}

				if (File.Exists(releaseOutput))
				{
					File.Delete(releaseOutput);
				}
			}
		}
		finally
		{
			Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(debugRoot))!)!,
				recursive: true);
			Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(releaseRoot))!)!,
				recursive: true);
		}
	}

	private static void WriteManifestInto(string directory)
	{
		File.WriteAllText(Path.Combine(directory, ManifestFixtures.EntrypointFileName), string.Empty);
		File.WriteAllText(Path.Combine(directory, PluginArtifactFiles.ManifestFileName),
			ManifestFixtures.ValidManifestJson());
	}
}
