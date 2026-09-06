using MacroDeck.Plugin.Cli.Scaffolding;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>Every <see cref="PluginScaffoldFailureReason" /> maps to a documented exit code and a distinct
/// kebab code - mirroring <see cref="ExitCodeMappingTests" /> - plus the exact codes issue #589's
/// acceptance table pins by name.</summary>
[TestFixture]
public class PluginScaffoldExitCodeTests
{
	private static readonly int[] _documentedExitCodes =
	[
		ExitCode.Success, ExitCode.SubjectInvalid, ExitCode.UsageError, ExitCode.InputUnreadable,
		ExitCode.Cancelled, ExitCode.InternalError
	];

	[Test]
	public void Every_failure_reason_maps_to_a_documented_exit_code()
	{
		Assert.Multiple(() =>
		{
			foreach (var reason in Enum.GetValues<PluginScaffoldFailureReason>())
			{
				Assert.That(_documentedExitCodes,
					Does.Contain(PluginScaffoldFailureExitCode.For(reason)),
					$"{reason} mapped to an undocumented exit code.");
			}
		});
	}

	[Test]
	public void Every_failure_reason_has_a_distinct_kebab_code()
	{
		var codes = Enum.GetValues<PluginScaffoldFailureReason>()
			.Select(reason => PluginScaffoldFailureCode.For(reason))
			.ToList();

		Assert.That(codes, Is.Unique);
	}

	[Test]
	public void Template_install_failure_is_input_unreadable()
	{
		Assert.That(PluginScaffoldFailureExitCode.For(PluginScaffoldFailureReason.TemplateInstallFailed),
			Is.EqualTo(ExitCode.InputUnreadable));
	}

	[Test]
	public void Dotnet_not_found_is_input_unreadable()
	{
		Assert.That(PluginScaffoldFailureExitCode.For(PluginScaffoldFailureReason.DotnetNotFound),
			Is.EqualTo(ExitCode.InputUnreadable));
	}

	[Test]
	public void Template_create_failure_is_subject_invalid()
	{
		Assert.That(PluginScaffoldFailureExitCode.For(PluginScaffoldFailureReason.TemplateCreateFailed),
			Is.EqualTo(ExitCode.SubjectInvalid));
	}
}
