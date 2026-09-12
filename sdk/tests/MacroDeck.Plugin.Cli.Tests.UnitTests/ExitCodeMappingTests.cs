using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Plugin.Testing.Conformance;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>
/// The exit-code mappings are total: every documented shape lands on one of the six documented codes
/// (0, 1, 2, 3, 4, 70 - see <see cref="ExitCode" />), never an undocumented one. A handful of mappings are
/// pinned to the exact code the issue's own requirement text calls out by name (a missing file, a
/// cancellation); the rest are checked only for landing in the documented set, not for which one
/// specifically - that finer categorisation is this implementation's own judgement call, not something
/// the requirement dictates, and pinning it here would just be copying <see cref="PluginInstallErrorExitCode" />'s
/// own branching back into its test.
/// </summary>
[TestFixture]
public class ExitCodeMappingTests
{
	private static readonly int[] _documentedExitCodes =
	[
		ExitCode.Success, ExitCode.SubjectInvalid, ExitCode.UsageError, ExitCode.InputUnreadable,
		ExitCode.Cancelled, ExitCode.InternalError
	];

	[Test]
	public void Every_PluginInstallError_maps_to_a_documented_exit_code()
	{
		Assert.Multiple(() =>
		{
			foreach (var error in Enum.GetValues<PluginInstallError>())
			{
				Assert.That(_documentedExitCodes,
					Does.Contain(PluginInstallErrorExitCode.For(error)),
					$"{error} mapped to an undocumented exit code.");
			}
		});
	}

	[Test]
	public void A_missing_or_unreadable_artifact_is_input_unreadable_not_subject_invalid()
	{
		// Pinned directly to the exit code table's own examples for code 3: "a missing file, not a ZIP".
		Assert.Multiple(() =>
		{
			Assert.That(PluginInstallErrorExitCode.For(PluginInstallError.ArtifactNotFound),
				Is.EqualTo(ExitCode.InputUnreadable));
			Assert.That(PluginInstallErrorExitCode.For(PluginInstallError.InvalidArchive),
				Is.EqualTo(ExitCode.InputUnreadable));
		});
	}

	[Test]
	public void A_cancelled_install_operation_maps_to_the_cancelled_exit_code()
	{
		Assert.That(PluginInstallErrorExitCode.For(PluginInstallError.Cancelled), Is.EqualTo(ExitCode.Cancelled));
	}

	[Test]
	public void Every_PluginManifestError_maps_to_a_documented_exit_code()
	{
		Assert.Multiple(() =>
		{
			foreach (var error in Enum.GetValues<PluginManifestError>())
			{
				Assert.That(_documentedExitCodes,
					Does.Contain(PluginManifestErrorExitCode.For(error)),
					$"{error} mapped to an undocumented exit code.");
			}
		});
	}

	[Test]
	public void No_PluginManifestError_falls_through_to_internal_error()
	{
		Assert.Multiple(() =>
		{
			foreach (var error in Enum.GetValues<PluginManifestError>())
			{
				Assert.That(PluginManifestErrorExitCode.For(error),
					Is.Not.EqualTo(ExitCode.InternalError),
					$"{error} has no explicit exit code mapping.");
			}
		});
	}

	[Test]
	public void A_missing_manifest_file_is_input_unreadable()
	{
		Assert.That(PluginManifestErrorExitCode.For(PluginManifestError.NotFound),
			Is.EqualTo(ExitCode.InputUnreadable));
	}

	[Test]
	public void A_conformant_report_exits_success()
	{
		Assert.That(ConformanceReportExitCode.For(MinimalReport(conformant: true)), Is.EqualTo(ExitCode.Success));
	}

	[Test]
	public void A_non_conformant_report_exits_subject_invalid()
	{
		Assert.That(ConformanceReportExitCode.For(MinimalReport(conformant: false)),
			Is.EqualTo(ExitCode.SubjectInvalid));
	}

	private static ConformanceReport MinimalReport(bool conformant)
	{
		return new ConformanceReport
		{
			SuiteVersion = ConformanceRunner.SuiteVersion,
			StartedAt = DateTimeOffset.UtcNow,
			Duration = TimeSpan.Zero,
			Results = [],
			Passed = 0,
			Failed = 0,
			Skipped = 0,
			Conformant = conformant
		};
	}
}
