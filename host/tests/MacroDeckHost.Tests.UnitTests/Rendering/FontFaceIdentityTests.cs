using MacroDeckHost.Application.Rendering;

namespace MacroDeckHost.Tests.UnitTests.Rendering;

[TestFixture]
public class FontFaceIdentityTests
{
	// A face id is persisted in every profile that configures a widget font, so the format is a stored
	// data contract: changing it orphans the font of every existing widget. These literals are the
	// contract, not a description of the current implementation.
	[TestCase("PT Sans", 400, 5, "upright", ExpectedResult = "pt-sans-400-5-upright")]
	[TestCase("Helvetica Neue", 700, 5, "italic", ExpectedResult = "helvetica-neue-700-5-italic")]
	[TestCase(".SF NS Display", 300, 3, "oblique", ExpectedResult = "sf-ns-display-300-3-oblique")]
	[TestCase("  Iowan Old Style  ", 400, 5, "upright", ExpectedResult = "iowan-old-style-400-5-upright")]
	[TestCase("Ünïcödé Fönt", 400, 5, "upright", ExpectedResult = "n-c-d-f-nt-400-5-upright")]
	[TestCase("Arial  Black", 900, 5, "upright", ExpectedResult = "arial-black-900-5-upright")]
	[TestCase("Avenir Next Condensed", 400, 3, "upright", ExpectedResult = "avenir-next-condensed-400-3-upright")]
	public string Build_ForAFamilyName_ProducesTheStoredIdFormat(string family, int weight, int width, string slant)
		=> FontFaceIdentity.Build(family, weight, width, slant);

	[TestCase("Roboto", false, false, ExpectedResult = "roboto-400-5-upright")]
	[TestCase("Roboto", true, false, ExpectedResult = "roboto-700-5-upright")]
	[TestCase("Roboto", false, true, ExpectedResult = "roboto-400-5-italic")]
	[TestCase("Roboto", true, true, ExpectedResult = "roboto-700-5-italic")]
	public string BuildLegacy_ForAFamilyBoldItalicTriple_ProducesTheStoredIdFormat(string family,
		bool bold,
		bool italic)
		=> FontFaceIdentity.BuildLegacy(family, bold, italic);

	[Test]
	public void Build_ForFamiliesWithNoAsciiLetters_KeepsThemApartWithoutRelyingOnEnumerationOrder()
	{
		// DirectWrite reports CJK family names in the system locale, so on a CJK-locale Windows these
		// are ordinary family names, not a curiosity.
		var first = FontFaceIdentity.Build("日本語フォント", 400, 5, "upright");
		var second = FontFaceIdentity.Build("한국어 글꼴", 400, 5, "upright");

		Assert.Multiple(() =>
		{
			Assert.That(first, Is.Not.EqualTo(second));
			Assert.That(first, Is.EqualTo(FontFaceIdentity.Build("日本語フォント", 400, 5, "upright")));
			Assert.That(first, Does.EndWith("-400-5-upright"));
			Assert.That(first, Does.Not.StartWith("-"));
		});
	}

	[Test]
	public void Build_ForAnyFamilyName_ProducesAnIdSafeInAUrlPathAndACssIdentifier()
	{
		string[] hostile =
		[
			"Font/With\\Slashes",
			"../../etc/passwd",
			"Font?query=1&x=2",
			"Font With \"Quotes\"",
			"日本語フォント",
			"   ",
			"-"
		];

		var ids = hostile.Select(family => FontFaceIdentity.Build(family, 400, 5, "upright")).ToList();

		Assert.That(ids.Where(id => !IsSafe(id)), Is.Empty, "every id must contain only [a-z0-9-]");
	}

	private static bool IsSafe(string id) =>
		id.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');
}
