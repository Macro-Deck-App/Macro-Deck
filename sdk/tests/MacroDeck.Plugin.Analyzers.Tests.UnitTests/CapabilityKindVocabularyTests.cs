using MacroDeck.Plugin.Protocol.Handshake;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests;

/// <summary>
/// The analyzer hardcodes the ten capability kinds rather than sharing them with
/// MacroDeck.Plugin.Protocol (an analyzer cannot execute the target framework's code to read a static
/// field's value even if it referenced the assembly - see CapabilityKindVocabulary's remarks). This drift
/// test is what keeps that hardcoded copy honest.
/// </summary>
[TestFixture]
public class CapabilityKindVocabularyTests
{
	[Test]
	public void The_hardcoded_vocabulary_matches_CapabilityKinds_All()
	{
		Assert.That(CapabilityKindVocabulary.All, Is.EqualTo(CapabilityKinds.All));
	}
}
