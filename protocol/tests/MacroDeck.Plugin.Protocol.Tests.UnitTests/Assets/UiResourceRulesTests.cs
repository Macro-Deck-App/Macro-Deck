using MacroDeck.Plugin.Protocol.Assets;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Assets;

[TestFixture]
public class UiResourceRulesTests
{
	private static readonly string[] _publishedKinds = ["icon", "artwork", "action-icon", "ui-resource", "icon-pack"];

	[TestCase("photo")]
	[TestCase("a")]
	[TestCase("Photo_2-large")]
	[TestCase("0123456789012345678901234567890123456789012345678901234567890123")]
	public void A_letter_or_digit_followed_by_letters_digits_hyphens_or_underscores_is_a_valid_name(string name)
		=> Assert.That(UiResourceRules.IsValidName(name), Is.True);

	[TestCase(null)]
	[TestCase("")]
	[TestCase("-photo")]
	[TestCase("_photo")]
	[TestCase("photo.png")]
	[TestCase("photo frame")]
	[TestCase("photo/1")]
	[TestCase("01234567890123456789012345678901234567890123456789012345678901234")]
	public void Anything_else_is_not_a_valid_name(string? name)
		=> Assert.That(UiResourceRules.IsValidName(name), Is.False);

	[TestCase("image/png")]
	[TestCase("image/jpeg")]
	[TestCase("image/webp")]
	[TestCase("image/gif")]
	[TestCase("IMAGE/PNG")]
	public void Raster_images_are_supported(string mediaType)
		=> Assert.That(UiResourceRules.IsSupportedMediaType(mediaType), Is.True);

	[TestCase(null)]
	[TestCase("image/svg+xml")]
	[TestCase("text/html")]
	[TestCase("application/octet-stream")]
	public void Other_media_types_are_not_supported(string? mediaType)
		=> Assert.That(UiResourceRules.IsSupportedMediaType(mediaType), Is.False);

	[Test]
	public void The_published_asset_kinds_keep_their_values()
		=> Assert.That(AssetKinds.All, Is.EqualTo(_publishedKinds));
}
