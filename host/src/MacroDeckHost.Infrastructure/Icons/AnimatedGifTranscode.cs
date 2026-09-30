using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;

namespace MacroDeckHost.Infrastructure.Icons;

/// <summary>
/// Prepares a decoded animation for the GIF encoder.
///
/// <para>
/// WebP and GIF disagree about what a transparent pixel means, and the disagreement is silent. A WebP
/// frame says how it blends with what is under it; a GIF frame cannot say anything of the sort, so a
/// transparent pixel there always shows the frame before unless that frame's disposal cleared the
/// canvas first. Every frame ImageSharp hands over has already been composited into a full picture with
/// transparent surroundings, so left at the encoder's default each frame stacked onto its predecessor:
/// a logo that draws itself came out as every stroke it had ever drawn, at once, and never cleared.
/// </para>
/// </summary>
internal static class AnimatedGifTranscode
{
	private static readonly byte[] LoopBlockStart =
	[
		0x21, 0xFF, 0x0B, (byte)'N', (byte)'E', (byte)'T', (byte)'S', (byte)'C', (byte)'A', (byte)'P', (byte)'E',
		(byte)'2', (byte)'.', (byte)'0', 0x03, 0x01
	];

	/// <summary>
	/// Says that each frame starts from a clear canvas, which is what makes a composited frame mean the
	/// same thing in a GIF as it does in the WebP it came from - and carries the timing across with it.
	///
	/// <para>
	/// The timing has to be copied here rather than left to the encoder, because asking a frame for its
	/// GIF metadata is what creates it: on an image decoded from WebP there is none until this runs, and
	/// what it creates carries a zero delay. Setting the disposal alone therefore replaced the delay the
	/// encoder would have derived from the WebP with nothing at all, and a zero-delay GIF is one every
	/// reader clamps to a floor of its own - the animation came out several times too slow.
	/// </para>
	/// </summary>
	private static void PrepareForGif(Image<Rgba32> image)
	{
		foreach (var frame in image.Frames)
		{
			SnapToSingleBitAlpha(frame);

			var delayMs = frame.Metadata.GetWebpMetadata().FrameDelay;
			var gif = frame.Metadata.GetGifMetadata();

			gif.DisposalMethod = GifDisposalMethod.RestoreToBackground;
			// GIF counts in hundredths of a second, WebP in milliseconds. Never rounded down to zero:
			// a frame that asked to be shown at all must not ask to be shown for no time.
			gif.FrameDelay = delayMs == 0 ? gif.FrameDelay : Math.Max(1, (int)Math.Round(delayMs / 10d));
		}
	}

	public static ushort ReadPlays(ReadOnlySpan<byte> gif)
	{
		var at = FindLoopCount(gif);
		if (at < 0)
		{
			return 1;
		}

		var repeats = gif[at] | (gif[at + 1] << 8);
		return repeats == 0 ? (ushort)0 : (ushort)Math.Min(repeats + 1, ushort.MaxValue);
	}

	public static async Task SaveAsGifAsync(Image<Rgba32> image, Stream output, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(image);
		ArgumentNullException.ThrowIfNull(output);

		var plays = image.Metadata.GetWebpMetadata().RepeatCount;
		PrepareForGif(image);
		image.Metadata.GetGifMetadata().RepeatCount = plays == 1 ? (ushort)1 : (ushort)0;

		using var encoded = new MemoryStream();
		await image.SaveAsGifAsync(encoded, new GifEncoder(), cancellationToken);
		var gif = encoded.ToArray();

		if (plays >= 2)
		{
			// The encoder writes no loop extension for a repeat count of 1, so the count is patched
			// into the block it wrote for 0.
			var at = FindLoopCount(gif);
			if (at >= 0)
			{
				var repeats = plays - 1;
				gif[at] = (byte)repeats;
				gif[at + 1] = (byte)(repeats >> 8);
			}
		}

		await output.WriteAsync(gif, cancellationToken);
	}

	private static int FindLoopCount(ReadOnlySpan<byte> gif)
	{
		var start = gif.IndexOf(LoopBlockStart);
		if (start < 0)
		{
			return -1;
		}

		var count = start + LoopBlockStart.Length;
		return count + 2 < gif.Length && gif[count + 2] == 0 ? count : -1;
	}

	// The GIF quantizer keeps only an all-zero pixel transparent: a transparent pixel that still carries
	// a colour, or a partly transparent one, is written opaque.
	private static void SnapToSingleBitAlpha(ImageFrame<Rgba32> frame)
	{
		frame.ProcessPixelRows(accessor =>
		{
			for (var y = 0; y < accessor.Height; y++)
			{
				foreach (ref var pixel in accessor.GetRowSpan(y))
				{
					pixel = pixel.A >= 128 ? pixel with { A = byte.MaxValue } : default;
				}
			}
		});
	}
}
