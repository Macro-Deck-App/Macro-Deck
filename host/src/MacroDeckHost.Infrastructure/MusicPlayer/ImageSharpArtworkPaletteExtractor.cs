using System.Globalization;
using MacroDeckHost.Application.MusicPlayer;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.MusicPlayer;

/// <summary>
/// The average-colour derivation the retired Angular widget did on a 16x16 <c>&lt;canvas&gt;</c>, moved
/// host-side unchanged: the cover is squashed to 16x16, its 256 pixels are averaged, and the pair is the
/// average darkened by half and lightened two fifths of the way to white. The numbers are deliberately
/// the same ones - the widget is meant to keep the colours it had.
/// </summary>
public sealed class ImageSharpArtworkPaletteExtractor : IArtworkPaletteExtractor
{
	private const int SampleEdge = 16;
	private const int MaxImageBytes = 32 * 1024 * 1024;

	private readonly ILogger _logger;

	public ImageSharpArtworkPaletteExtractor(ILogger logger)
	{
		_logger = logger;
	}

	public ArtworkPalette? Extract(byte[] image)
	{
		ArgumentNullException.ThrowIfNull(image);

		if (image.Length == 0 || image.Length > MaxImageBytes)
		{
			return null;
		}

		try
		{
			return Average(image);
		}
#pragma warning disable CA1031 // Artwork is provider-owned; undecodable bytes leave the widget on its theme.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			_logger.Debug(exception, "Artwork colours could not be derived; falling back to the theme");

			return null;
		}
	}

	private static ArtworkPalette Average(byte[] bytes)
	{
		using var image = Image.Load<Rgba32>(bytes);

		// Stretched, not fitted: the canvas this replaces drew the whole cover into a 16x16 box, so every
		// part of the artwork contributed to the average regardless of its shape.
		image.Mutate(context => context.Resize(SampleEdge, SampleEdge, KnownResamplers.Bicubic));

		long red = 0;
		long green = 0;
		long blue = 0;

		image.ProcessPixelRows(accessor =>
		{
			for (var y = 0; y < accessor.Height; y++)
			{
				foreach (ref var pixel in accessor.GetRowSpan(y))
				{
					red += pixel.R;
					green += pixel.G;
					blue += pixel.B;
				}
			}
		});

		var count = SampleEdge * SampleEdge;

		return new ArtworkPalette(Hex(Lighten(red / count), Lighten(green / count), Lighten(blue / count)),
			Hex(Darken(red / count), Darken(green / count), Darken(blue / count)));
	}

	private static int Darken(long channel) => (int)Math.Round(channel * 0.5, MidpointRounding.AwayFromZero);

	private static int Lighten(long channel)
		=> (int)Math.Round(channel + ((255 - channel) * 0.4), MidpointRounding.AwayFromZero);

	private static string Hex(int red, int green, int blue)
		=> string.Create(CultureInfo.InvariantCulture, $"#{red:x2}{green:x2}{blue:x2}");
}
