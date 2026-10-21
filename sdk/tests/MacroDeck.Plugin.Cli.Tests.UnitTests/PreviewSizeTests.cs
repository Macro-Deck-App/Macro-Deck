using MacroDeck.Plugin.Cli.Rendering;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

[TestFixture]
public class PreviewSizeTests
{
	[TestCase("1x1", 120, 120)]
	[TestCase("2x1", 252, 120)]
	[TestCase("3x2", 384, 252)]
	public void Deck_cells_are_120_pixels_with_a_12_pixel_gap(string text, int width, int height)
	{
		Assert.That(PreviewSize.TryParse(text, true, out var size), Is.True);
		Assert.That(size, Is.EqualTo(new PreviewSize(width, height, 120)));
	}

	[Test]
	public void Pixel_sizes_are_taken_as_written_and_drawn_as_one_tile()
	{
		Assert.That(PreviewSize.TryParse("420x200", false, out var size), Is.True);
		Assert.That(size, Is.EqualTo(new PreviewSize(420, 200, 200)));
	}

	[TestCase("")]
	[TestCase("200")]
	[TestCase("0x10")]
	[TestCase("-1x10")]
	[TestCase("axb")]
	[TestCase("10x10x10")]
	[TestCase("9000x10")]
	public void A_malformed_or_oversized_size_is_rejected(string text)
	{
		Assert.That(PreviewSize.TryParse(text, false, out _), Is.False);
	}

	[Test]
	public void An_oversized_cell_count_is_rejected()
	{
		Assert.That(PreviewSize.TryParse("100x1", true, out _), Is.False);
	}
}
