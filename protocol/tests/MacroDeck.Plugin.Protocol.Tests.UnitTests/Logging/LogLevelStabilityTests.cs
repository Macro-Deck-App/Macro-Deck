using MacroDeck.Plugin.Protocol.Logging;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Logging;

/// <summary>
/// Pins the log severity strings against a hard-coded literal - not derived from
/// <see cref="LogLevels.All" /> - the same discipline <c>MessageTypeStabilityTests</c> uses. Catches a
/// level string renamed on one side only, which would silently arrive at the wrong severity.
/// </summary>
[TestFixture]
public class LogLevelStabilityTests
{
	private static readonly string[] _expectedLevels =
	[
		"verbose",
		"debug",
		"information",
		"warning",
		"error",
		"fatal",
	];

	[Test]
	public void The_level_constants_match_the_frozen_literal()
	{
		string[] actualDeclarationOrder =
		[
			LogLevels.Verbose, LogLevels.Debug, LogLevels.Information, LogLevels.Warning, LogLevels.Error,
			LogLevels.Fatal
		];

		Assert.That(actualDeclarationOrder, Is.EqualTo(_expectedLevels));
	}

	[Test]
	public void All_contains_exactly_the_six_levels()
	{
		Assert.That(LogLevels.All, Is.EquivalentTo(_expectedLevels));
	}

	[Test]
	public void No_duplicate_levels()
		=> Assert.That(LogLevels.All, Is.Unique);
}
