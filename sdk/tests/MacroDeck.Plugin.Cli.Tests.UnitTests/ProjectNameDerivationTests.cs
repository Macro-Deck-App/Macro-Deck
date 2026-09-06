using MacroDeck.Plugin.Cli.Scaffolding;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

[TestFixture]
public class ProjectNameDerivationTests
{
	[TestCase("Spotify Controller", "SpotifyController")]
	[TestCase("OBS-Studio Bridge", "OBSStudioBridge")]
	[TestCase("3D Lights", "Plugin3DLights")]
	[TestCase("Grüne Lampen", "GrüneLampen")]
	public void Derive_matches_the_documented_rule(string name, string expected)
	{
		Assert.That(ProjectNameDerivation.Derive(name), Is.EqualTo(expected));
	}

	[Test]
	public void Derive_returns_null_when_nothing_alphanumeric_survives()
	{
		Assert.That(ProjectNameDerivation.Derive("！！！"), Is.Null);
	}

	[TestCase("SpotifyController", true)]
	[TestCase("Acme.LightControl", true)]
	[TestCase("9Lives", false)]
	[TestCase("", false)]
	[TestCase(null, false)]
	[TestCase("Has Space", false)]
	public void Validate_accepts_legal_identifiers_and_dotted_names(string? candidate, bool expected)
	{
		Assert.That(ProjectNameDerivation.Validate(candidate), Is.EqualTo(expected));
	}
}
