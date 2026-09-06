using MacroDeckHost.Infrastructure.MusicPlayer;
using Serilog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace MacroDeckHost.Tests.UnitTests.MusicPlayer;

/// <summary>
/// The Music Player's colours moved host-side with #749, and they are meant to be the <i>same</i>
/// colours: the retired Angular widget averaged the cover on a 16x16 canvas and derived a darkened
/// background and a lightened accent from it. These pin that derivation against stated inputs rather
/// than against whatever the implementation happens to compute.
/// </summary>
[TestFixture]
public class ImageSharpArtworkPaletteExtractorTests
{
	[Test]
	public void A_flat_cover_derives_its_pair_from_its_own_colour()
	{
		// 100 -> 50 darkened, and 100 + (255 - 100) * 0.4 = 162 lightened.
		var palette = Extract(new Rgba32(100, 100, 100));

		Assert.Multiple(() =>
		{
			Assert.That(palette!.Background, Is.EqualTo("#323232"));
			Assert.That(palette.Accent, Is.EqualTo("#a2a2a2"));
		});
	}

	[Test]
	public void Black_artwork_stays_black_behind_and_greys_in_front()
	{
		var palette = Extract(new Rgba32(0, 0, 0));

		Assert.Multiple(() =>
		{
			Assert.That(palette!.Background, Is.EqualTo("#000000"));
			// 0 + 255 * 0.4 = 102: an accent has to stay visible even on a black cover.
			Assert.That(palette.Accent, Is.EqualTo("#666666"));
		});
	}

	[Test]
	public void Each_channel_is_derived_on_its_own_so_a_tinted_cover_keeps_its_hue()
	{
		var palette = Extract(new Rgba32(200, 40, 90));

		Assert.Multiple(() =>
		{
			// 100, 20, 45 darkened.
			Assert.That(palette!.Background, Is.EqualTo("#64142d"));
			// 222, 126, 156 lightened.
			Assert.That(palette.Accent, Is.EqualTo("#de7e9c"));
		});
	}

	[Test]
	public void A_half_and_half_cover_averages_the_two_halves_rather_than_picking_one()
	{
		using var image = new Image<Rgba32>(64, 64);

		image.ProcessPixelRows(accessor =>
		{
			for (var y = 0; y < accessor.Height; y++)
			{
				var row = accessor.GetRowSpan(y);

				for (var x = 0; x < row.Length; x++)
				{
					row[x] = x < 32 ? new Rgba32(0, 0, 0) : new Rgba32(200, 200, 200);
				}
			}
		});

		var palette = Extract(image);

		Assert.Multiple(() =>
		{
			// The average is 100, so the pair matches the flat 100 cover above.
			Assert.That(palette!.Background, Is.EqualTo("#323232"));
			Assert.That(palette.Accent, Is.EqualTo("#a2a2a2"));
		});
	}

	[Test]
	public void Bytes_that_are_not_an_image_leave_the_widget_on_its_theme()
	{
		var extractor = new ImageSharpArtworkPaletteExtractor(new LoggerConfiguration().CreateLogger());

		Assert.Multiple(() =>
		{
			Assert.That(extractor.Extract([1, 2, 3, 4]), Is.Null);
			Assert.That(extractor.Extract([]), Is.Null);
		});
	}

	private static Application.MusicPlayer.ArtworkPalette? Extract(Rgba32 colour)
	{
		using var image = new Image<Rgba32>(64, 64, colour);

		return Extract(image);
	}

	private static Application.MusicPlayer.ArtworkPalette? Extract(Image<Rgba32> image)
	{
		using var stream = new MemoryStream();
		image.Save(stream, new PngEncoder());

		return new ImageSharpArtworkPaletteExtractor(new LoggerConfiguration().CreateLogger())
			.Extract(stream.ToArray());
	}
}
