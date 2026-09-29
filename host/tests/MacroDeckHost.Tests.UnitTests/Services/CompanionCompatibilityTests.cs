using MacroDeck.Plugin.Packaging.Versioning;
using MacroDeckHost.Application.Services;

namespace MacroDeckHost.Tests.UnitTests.Services;

public class CompanionCompatibilityTests
{
	[Test]
	public void Minimum_companion_version_is_a_semantic_version()
	{
		Assert.That(SemanticVersion.TryParse(CompanionCompatibility.MinimumCompanionVersion, out _), Is.True);
	}
}
