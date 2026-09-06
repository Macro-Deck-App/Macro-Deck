using System.Text.RegularExpressions;
using MacroDeck.Plugin.Cli.Signing;
using MacroDeck.Signing;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>
/// <see cref="SigningFailureCode" /> and <see cref="SigningFailureExitCode" /> are both declared total over
/// <see cref="SigningError" /> ("mirrors ..., total over the same enum for the same reason") - this pins
/// that every member actually has a kebab-case code and a defined exit code, so a new <see cref="SigningError" />
/// member added later cannot silently fall through to the catch-all <c>"signing-failed"</c> /
/// <see cref="ExitCode.InternalError" /> branch without a test noticing.
/// </summary>
[TestFixture]
public class SigningFailureMappingTests
{
	private static readonly Regex _kebabCase = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.None);

	private static readonly int[] _definedExitCodes =
	[
		ExitCode.Success,
		ExitCode.SubjectInvalid,
		ExitCode.UsageError,
		ExitCode.InputUnreadable,
		ExitCode.Cancelled,
		ExitCode.InternalError
	];

	private static IEnumerable<SigningError> AllSigningErrors => Enum.GetValues<SigningError>();

	[TestCaseSource(nameof(AllSigningErrors))]
	public void Every_SigningError_member_has_a_kebab_case_code(SigningError error)
	{
		var code = SigningFailureCode.For(error);

		Assert.Multiple(() =>
		{
			Assert.That(code, Does.Match(_kebabCase), $"'{code}' for {error} is not kebab-case");
			Assert.That(code,
				Is.Not.EqualTo("signing-failed"),
				$"{error} fell through to the catch-all code - it needs its own case in SigningFailureCode.For");
		});
	}

	[TestCaseSource(nameof(AllSigningErrors))]
	public void Every_SigningError_member_has_a_defined_exit_code(SigningError error)
	{
		var exitCode = SigningFailureExitCode.For(error);

		Assert.That(_definedExitCodes, Does.Contain(exitCode), $"{error} mapped to undefined exit code {exitCode}");
	}

	/// <summary>Every code produced by <see cref="SigningFailureCode" /> must be distinct - two different
	/// <see cref="SigningError" /> members sharing one code would make the CLI's diagnostics ambiguous.
	/// </summary>
	[Test]
	public void Every_SigningError_member_maps_to_a_distinct_code()
	{
		var codes = AllSigningErrors.Select(SigningFailureCode.For).ToList();

		Assert.That(codes, Is.Unique);
	}
}
