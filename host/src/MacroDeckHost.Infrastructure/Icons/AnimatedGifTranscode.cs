using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;

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
	public static void PrepareForGif(Image image)
	{
		ArgumentNullException.ThrowIfNull(image);

		var webp = image.Metadata.GetWebpMetadata();

		foreach (var frame in image.Frames)
		{
			var delayMs = frame.Metadata.GetWebpMetadata().FrameDelay;
			var gif = frame.Metadata.GetGifMetadata();

			gif.DisposalMethod = GifDisposalMethod.RestoreToBackground;
			// GIF counts in hundredths of a second, WebP in milliseconds. Never rounded down to zero:
			// a frame that asked to be shown at all must not ask to be shown for no time.
			gif.FrameDelay = delayMs == 0 ? gif.FrameDelay : Math.Max(1, (int)Math.Round(delayMs / 10d));
		}

		image.Metadata.GetGifMetadata().RepeatCount = webp.RepeatCount;
	}
}
