using MacroDeckHost.Integrations.SinusBot;

namespace MacroDeckHost.Tests.UnitTests.SinusBot;

[TestFixture]
internal sealed class SinusBotSlugTests
{
	[TestCase("FunkGerätesatz Fu 5", "funkgeraetesatz_fu_5")]
	[TestCase("Übungs-Böschung süß", "uebungs_boeschung_suess")]
	[TestCase("My Bot", "my_bot")]
	[TestCase("already_valid_key", "already_valid_key")]
	[TestCase("  Trim  Me  ", "trim_me")]
	[TestCase("---", "")]
	[TestCase("", "")]
	public void Slugify_TransliteratesUmlautsAndKeepsWordBoundaries(string title, string expected)
	{
		Assert.That(SinusBotIntegration.Slugify(title), Is.EqualTo(expected));
	}
}
