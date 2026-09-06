using System.Globalization;
using System.Text.RegularExpressions;
using MacroDeck.Plugin.Cli.Commands;
using MacroDeck.Plugin.Cli.Manifests;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>
/// <c>validate</c> as a function: <see cref="ManifestValidator.ValidateManifestFileAsync" /> mapped from
/// a manifest path straight to <c>(exitCode, problems)</c>, with no process ever spawned.
/// </summary>
[TestFixture]
public class ManifestValidatorTests
{
	[Test]
	public async Task A_well_formed_manifest_validates_clean_and_exits_success()
	{
		var directory = ManifestFixtures.WriteValidManifestDirectory();

		try
		{
			var result = await ManifestValidator.ValidateManifestFileAsync(Path.Combine(directory, "manifest.json"));

			Assert.Multiple(() =>
			{
				Assert.That(result.Valid, Is.True);
				Assert.That(result.ExitCode, Is.EqualTo(ExitCode.Success));
				Assert.That(result.PluginId, Is.EqualTo(ManifestFixtures.PluginId));
				Assert.That(result.Problems.Any(problem => problem.Severity == ManifestProblemSeverity.Error),
					Is.False);
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public async Task An_invalid_plugin_id_fails_validation_and_names_the_specific_problem()
	{
		var directory = ManifestFixtures.WriteInvalidIdManifestDirectory();

		try
		{
			var result = await ManifestValidator.ValidateManifestFileAsync(Path.Combine(directory, "manifest.json"));

			Assert.Multiple(() =>
			{
				Assert.That(result.Valid, Is.False);
				Assert.That(result.ExitCode, Is.EqualTo(ExitCode.SubjectInvalid));

				// Names the specific problem - reused verbatim from IPluginManifestReader, the real reader
				// this command runs - not merely "something failed".
				Assert.That(result.Problems,
					Has.Some.Matches<ManifestProblem>(problem =>
						problem.Severity == ManifestProblemSeverity.Error &&
						problem.Message.Contains("not a valid plugin id", StringComparison.OrdinalIgnoreCase)));
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public async Task A_missing_manifest_file_exits_input_unreadable_not_subject_invalid()
	{
		var missingPath = Path.Combine(Path.GetTempPath(),
			$"macrodeck-plugin-cli-tests-missing-{Guid.NewGuid():N}",
			"manifest.json");

		var result = await ManifestValidator.ValidateManifestFileAsync(missingPath);

		Assert.Multiple(() =>
		{
			Assert.That(result.Valid, Is.False);

			// The CI-usability distinction the exit code table exists for: a missing file is an
			// environment problem, never conflated with a genuinely invalid manifest.
			Assert.That(result.ExitCode, Is.EqualTo(ExitCode.InputUnreadable));
			Assert.That(result.ExitCode, Is.Not.EqualTo(ExitCode.SubjectInvalid));
		});
	}

	[Test]
	public async Task A_missing_artifact_file_also_exits_input_unreadable()
	{
		var missingPath = Path.Combine(Path.GetTempPath(),
			$"macrodeck-plugin-cli-tests-missing-{Guid.NewGuid():N}.macroDeckPlugin");

		var result = await ManifestValidator.ValidateArtifactAsync(missingPath);

		Assert.Multiple(() =>
		{
			Assert.That(result.Valid, Is.False);
			Assert.That(result.ExitCode, Is.EqualTo(ExitCode.InputUnreadable));
		});
	}

	[Test]
	public async Task Four_independent_manifest_problems_are_reported_in_one_run()
	{
		// The issue's own example: invalid id, empty name, an unusable version, and no entrypoints at all -
		// four independent defects that must all surface from one run, not one-at-a-time across four.
		var directory = ManifestFixtures.WriteFourIndependentProblemsManifestDirectory();

		try
		{
			var result = await ManifestValidator.ValidateManifestFileAsync(Path.Combine(directory, "manifest.json"));

			var errors = result.Problems.Where(p => p.Severity == ManifestProblemSeverity.Error).ToList();
			var distinctCodes = errors.Select(p => p.Code).Distinct().ToList();

			Assert.Multiple(() =>
			{
				Assert.That(result.Valid, Is.False);
				Assert.That(result.ExitCode, Is.EqualTo(ExitCode.SubjectInvalid));
				Assert.That(distinctCodes, Has.Count.GreaterThanOrEqualTo(4));

				// The id: reader-reported, kebab-cased from PluginManifestError.InvalidPluginId.
				Assert.That(errors, Has.Some.Matches<ManifestProblem>(p => p.Code == "invalid-plugin-id"));

				// The name: schema minLength on an empty string, pointing at /name.
				Assert.That(errors, Has.Some.Matches<ManifestProblem>(p => p.Pointer == "/name"));

				// The version: not SemVer, pointing at /version.
				Assert.That(errors,
					Has.Some.Matches<ManifestProblem>(p => p.Code == "invalid-version" &&
						p.Pointer == "/version"));

				// The entrypoints: schema minProperties on an empty object, pointing at /entrypoints.
				Assert.That(errors, Has.Some.Matches<ManifestProblem>(p => p.Pointer == "/entrypoints"));
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public async Task An_invalid_id_is_reported_once_with_the_specific_message()
	{
		var directory = ManifestFixtures.WriteInvalidIdManifestDirectory();

		try
		{
			var result = await ManifestValidator.ValidateManifestFileAsync(Path.Combine(directory, "manifest.json"));

			// One defect must read as one problem. The reader and the schema both object to a bad id, so
			// reporting every layer verbatim would show it twice - and the surviving one has to be the
			// reader's specific sentence, not the schema's generic "pattern" text. Selected by what the
			// problems say rather than by pointer: only schema-sourced problems carry one, which
			// cli.md documents ("a schema pointer when there is one").
			var idProblems = result.Problems
				.Where(problem =>
					problem.Message.Contains(ManifestFixtures.InvalidPluginId, StringComparison.Ordinal) ||
					problem.Pointer == "/id")
				.ToList();

			Assert.Multiple(() =>
			{
				Assert.That(idProblems, Has.Count.EqualTo(1));
				Assert.That(idProblems[0].Message,
					Does.Contain("not a valid plugin id").IgnoreCase);
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public async Task A_valid_part_of_an_invalid_manifest_is_never_reported_as_a_problem()
	{
		var directory = ManifestFixtures.WriteUnsupportedManifestVersionDirectory();

		try
		{
			var result = await ManifestValidator.ValidateManifestFileAsync(Path.Combine(directory, "manifest.json"));

			// Reporting every layer at once must not mean inventing findings. The entrypoint here is legal and
			// its file exists, so nothing may be said about it - and the one real defect must be reported once,
			// not once by the reader and again by the schema.
			Assert.Multiple(() =>
			{
				Assert.That(result.ExitCode, Is.EqualTo(ExitCode.SubjectInvalid));
				Assert.That(result.Problems,
					Has.None.Matches<ManifestProblem>(p =>
						p.Pointer?.StartsWith("/entrypoints", StringComparison.Ordinal) == true),
					"a legal, present entrypoint must not be reported as a problem");
				Assert.That(result.Problems.Count(p => p.Severity == ManifestProblemSeverity.Error),
					Is.EqualTo(1),
					"one defect must read as one problem");
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public async Task A_version_that_is_not_semver_is_an_error()
	{
		var directory = ManifestFixtures.WriteManifestDirectoryWithVersion("not-a-version");

		try
		{
			var result = await ManifestValidator.ValidateManifestFileAsync(Path.Combine(directory, "manifest.json"));

			Assert.Multiple(() =>
			{
				Assert.That(result.ExitCode, Is.EqualTo(ExitCode.SubjectInvalid));
				Assert.That(result.Problems,
					Has.Some.Matches<ManifestProblem>(p =>
						p.Severity == ManifestProblemSeverity.Error &&
						p.Code == "invalid-version" &&
						p.Message.Contains("not-a-version")));
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[TestCase("1.0.0")]
	[TestCase("10.20.30")]
	[TestCase("1.0.0-rc.1")]
	[TestCase("1.0.0+build.5")]
	[TestCase("3.0.0-preview.1+7a82b551")]
	public async Task A_real_semver_version_is_not_flagged(string version)
	{
		var directory = ManifestFixtures.WriteManifestDirectoryWithVersion(version);

		try
		{
			var result = await ManifestValidator.ValidateManifestFileAsync(Path.Combine(directory, "manifest.json"));

			Assert.Multiple(() =>
			{
				Assert.That(result.Valid, Is.True);
				Assert.That(result.Problems, Has.None.Matches<ManifestProblem>(p => p.Code == "invalid-version"));
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	/// <summary>Catches the length bound existing only in the host: without the schema's <c>maxLength: 128</c>
	/// (A2b), <c>macrodeck-plugin validate</c> would green-light a name the host's reader then refuses.</summary>
	[Test]
	public async Task An_over_long_name_fails_cli_validation_and_a_128_character_name_does_not()
	{
		var overLongDirectory = ManifestFixtures.WriteManifestDirectoryWithName(new string('a', 129));
		var atLimitDirectory = ManifestFixtures.WriteManifestDirectoryWithName(new string('a', 128));

		try
		{
			var overLongResult =
				await ManifestValidator.ValidateManifestFileAsync(Path.Combine(overLongDirectory, "manifest.json"));
			var atLimitResult =
				await ManifestValidator.ValidateManifestFileAsync(Path.Combine(atLimitDirectory, "manifest.json"));

			Assert.Multiple(() =>
			{
				Assert.That(overLongResult.Valid, Is.False);
				Assert.That(overLongResult.ExitCode, Is.EqualTo(ExitCode.SubjectInvalid));

				Assert.That(atLimitResult.Valid,
					Is.True,
					atLimitResult.Problems.Count > 0 ? atLimitResult.Problems[0].Message : null);
			});
		}
		finally
		{
			Directory.Delete(overLongDirectory, recursive: true);
			Directory.Delete(atLimitDirectory, recursive: true);
		}
	}

	[Test]
	public async Task Malformed_json_names_the_file_and_a_position_that_tracks_the_error()
	{
		var earlyDirectory = ManifestFixtures.WriteEarlyMalformedManifestDirectory();
		var unterminatedDirectory = ManifestFixtures.WriteUnterminatedMalformedManifestDirectory();

		try
		{
			var earlyPath = Path.Combine(earlyDirectory, "manifest.json");
			var unterminatedPath = Path.Combine(unterminatedDirectory, "manifest.json");

			var earlyResult = await ManifestValidator.ValidateManifestFileAsync(earlyPath);
			var unterminatedResult = await ManifestValidator.ValidateManifestFileAsync(unterminatedPath);

			var earlyMessage = earlyResult.Problems.Single().Message;
			var unterminatedMessage = unterminatedResult.Problems.Single().Message;

			var earlyPosition = ExtractPosition(earlyMessage);
			var unterminatedPosition = ExtractPosition(unterminatedMessage);

			Assert.Multiple(() =>
			{
				Assert.That(earlyResult.ExitCode, Is.EqualTo(ExitCode.SubjectInvalid));
				Assert.That(unterminatedResult.ExitCode, Is.EqualTo(ExitCode.SubjectInvalid));

				Assert.That(earlyMessage, Does.Contain(earlyPath));
				Assert.That(unterminatedMessage, Does.Contain(unterminatedPath));

				Assert.That(earlyPosition, Is.Not.Null);
				Assert.That(unterminatedPosition, Is.Not.Null);
				Assert.That(earlyPosition, Is.Not.EqualTo(unterminatedPosition));

				// The counterexample position 1:0 (line 1, byte 0) issue #556 calls out by name: an
				// end-of-file failure on a multi-line document must never be reported as line 1.
				Assert.That(unterminatedPosition!.Value.Line, Is.Not.EqualTo(1));
			});
		}
		finally
		{
			Directory.Delete(earlyDirectory, recursive: true);
			Directory.Delete(unterminatedDirectory, recursive: true);
		}
	}

	[Test]
	public async Task A_manifest_beside_a_project_file_is_diagnosed_as_a_source_directory()
	{
		var directory = ManifestFixtures.WriteSourceDirectoryManifestDirectory();

		try
		{
			var result = await ManifestValidator.ValidateManifestFileAsync(Path.Combine(directory, "manifest.json"));
			var problem = result.Problems.Single();

			Assert.Multiple(() =>
			{
				Assert.That(result.ExitCode, Is.EqualTo(ExitCode.SubjectInvalid));
				Assert.That(problem.Code, Is.EqualTo("source-directory"));
				Assert.That(problem.Message, Does.Contain("source directory"));
				Assert.That(problem.Message, Does.Contain("bin/Release"));
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public async Task A_build_output_directory_with_a_missing_entrypoint_is_not_called_a_source_directory()
	{
		var directory = ManifestFixtures.WriteMissingEntrypointManifestDirectory();

		try
		{
			var result = await ManifestValidator.ValidateManifestFileAsync(Path.Combine(directory, "manifest.json"));
			var problem = result.Problems.Single();

			Assert.Multiple(() =>
			{
				Assert.That(result.ExitCode, Is.EqualTo(ExitCode.SubjectInvalid));
				Assert.That(problem.Code, Is.EqualTo("entrypoint-missing"));
				Assert.That(problem.Message, Does.Not.Contain("source directory"));
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public async Task A_file_that_is_not_a_zip_is_named_and_the_dotnet_exception_never_leaks()
	{
		var artifactPath = Path.Combine(Path.GetTempPath(),
			$"macrodeck-plugin-cli-tests-{Guid.NewGuid():N}.macroDeckPlugin");
		await File.WriteAllBytesAsync(artifactPath, "this is definitely not a zip archive"u8.ToArray());

		try
		{
			var result = await ManifestValidator.ValidateArtifactAsync(artifactPath);
			var problem = result.Problems.Single();

			Assert.Multiple(() =>
			{
				Assert.That(result.ExitCode, Is.EqualTo(ExitCode.InputUnreadable));
				Assert.That(problem.Code, Is.EqualTo("not-an-artifact"));
				Assert.That(problem.Message, Does.Contain(artifactPath));
				Assert.That(problem.Message, Does.Contain("not a .macroDeckPlugin artifact"));

				Assert.That(problem.Message, Does.Not.Contain("End of Central Directory"));
				Assert.That(problem.Message, Does.Not.Contain("Central Directory corrupt"));
				Assert.That(problem.Message, Does.Not.Contain("InvalidArchive"));
			});
		}
		finally
		{
			File.Delete(artifactPath);
		}
	}

	[Test]
	public async Task A_manifest_passed_as_an_artifact_points_at_validate_manifest()
	{
		var directory = ManifestFixtures.WriteValidManifestDirectory();
		var manifestPath = Path.Combine(directory, "manifest.json");

		try
		{
			var result = await ManifestValidator.ValidateArtifactAsync(manifestPath);
			var problem = result.Problems.Single();

			Assert.That(problem.Message, Does.Contain("validate --manifest"));
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public async Task A_non_zip_not_named_manifest_json_gets_no_validate_hint()
	{
		var artifactPath = Path.Combine(Path.GetTempPath(),
			$"macrodeck-plugin-cli-tests-{Guid.NewGuid():N}.macroDeckPlugin");
		await File.WriteAllBytesAsync(artifactPath, "still not a zip archive"u8.ToArray());

		try
		{
			var result = await ManifestValidator.ValidateArtifactAsync(artifactPath);
			var problem = result.Problems.Single();

			Assert.Multiple(() =>
			{
				Assert.That(problem.Message, Does.Not.Contain("validate --manifest"));
				Assert.That(problem.Message, Does.Not.Contain("Did you mean"));
			});
		}
		finally
		{
			File.Delete(artifactPath);
		}
	}

	[Test]
	public async Task The_summary_line_names_the_subject_rather_than_echoing_an_unreadable_id()
	{
		var directory = ManifestFixtures.WriteInvalidIdManifestDirectory();

		try
		{
			var result = await ManifestValidator.ValidateManifestFileAsync(Path.Combine(directory, "manifest.json"));

			var output = new StringWriter();
			var error = new StringWriter();
			var console = new CliConsole(Verbosity.Normal, noColor: true, output, error);

			ValidationResultWriter.Write(console, CliOutputFormat.Text, result);

			// The issue's own complaint is specifically about the summary/heading line - "<id> <version>: N
			// error(s), N warning(s)." - echoing an id that failed validation in the first place, not about
			// the per-problem lines above it (one of which legitimately names the bad id as the specific
			// defect it found, exactly like An_invalid_plugin_id_fails_validation_and_names_the_specific_problem
			// expects). Only the final, non-empty line is the summary.
			var summaryLine = output.ToString()
				.Split('\n', StringSplitOptions.RemoveEmptyEntries)
				.Last()
				.TrimEnd('\r');

			Assert.Multiple(() =>
			{
				Assert.That(summaryLine, Does.Contain(Path.Combine(directory, "manifest.json")));
				Assert.That(summaryLine, Does.Not.Contain("Not A Valid Id"));
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	private static (int Line, int Position)? ExtractPosition(string message)
	{
		var match = Regex.Match(message, @"\(line (\d+), position (\d+)\)");
		return match.Success
			? (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
				int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture))
			: null;
	}
}
