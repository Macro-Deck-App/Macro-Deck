using MacroDeckHost.Infrastructure.Icons;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace MacroDeckHost.Tests.UnitTests.Icons;

/// <summary>
/// An animated icon is stored as WebP and transcoded to GIF for readers that cannot take one. The two
/// formats disagree about what a transparent pixel means, and that disagreement is silent: WebP says
/// how a frame blends with what is under it, GIF has no such control at all - a transparent pixel there
/// always shows the frame before unless the frame's disposal clears the canvas first.
///
/// <para>
/// So a run of frames that are full pictures with transparent surroundings - which is what every frame
/// becomes once it has been composited - stacks up in the GIF instead of replacing. A logo that draws
/// itself came out as every stroke it had ever drawn, all at once.
/// </para>
/// </summary>
[TestFixture]
public class AnimatedIconTranscodeTests
{
	/// <summary>
	/// Three frames, each a full canvas holding one opaque square in a different place and transparent
	/// everywhere else - the shape a composited animation has.
	/// </summary>
	private static Image<Rgba32> ThreeMovingSquares()
	{
		var image = new Image<Rgba32>(32, 32);
		Paint(image.Frames.RootFrame, 0);

		for (var index = 1; index < 3; index++)
		{
			using var next = new Image<Rgba32>(32, 32);
			Paint(next.Frames.RootFrame, index);
			image.Frames.AddFrame(next.Frames.RootFrame);
		}

		return image;
	}

	private static void Paint(ImageFrame<Rgba32> frame, int index)
	{
		for (var y = 0; y < 8; y++)
		{
			for (var x = 0; x < 8; x++)
			{
				frame[(index * 10) + x, y] = new Rgba32(255, 255, 255, 255);
			}
		}
	}

	private static int Opaque(ImageFrame<Rgba32> frame)
	{
		var count = 0;
		for (var y = 0; y < frame.Height; y++)
		{
			for (var x = 0; x < frame.Width; x++)
			{
				if (frame[x, y].A > 8)
				{
					count++;
				}
			}
		}

		return count;
	}

	/// <summary>
	/// Every frame holds exactly one square, so every frame of the transcode must too. A frame that also
	/// carries the squares before it is the previous frame showing through - the reader sees a trail
	/// rather than a moving square, and it never clears for as long as the animation runs.
	/// </summary>
	[Test]
	public async Task GifTranscodeDoesNotStackEachFrameOnTheOneBeforeIt()
	{
		using var source = ThreeMovingSquares();

		// What ImageSharpIconProcessor.CopyAnimationMetadata writes: a composited frame replaces the
		// canvas rather than blending onto it, which is what makes the stored WebP decode faithfully.
		foreach (var frame in source.Frames)
		{
			var webpFrame = frame.Metadata.GetWebpMetadata();
			webpFrame.BlendMethod = WebpBlendMethod.Source;
			webpFrame.DisposalMethod = WebpDisposalMethod.DoNotDispose;
			webpFrame.FrameDelay = 30;
		}

		using var webp = new MemoryStream();
		await source.SaveAsync(webp, new WebpEncoder { FileFormat = WebpFileFormatType.Lossless });

		webp.Position = 0;
		using var stored = await Image.LoadAsync<Rgba32>(webp);
		Assert.That(stored.Frames.Select(f => Opaque((ImageFrame<Rgba32>)f)),
			Is.EqualTo(new[] { 64, 64, 64 }),
			"the stored WebP itself must decode faithfully, or the GIF step is not what is being tested");
		using var gif = new MemoryStream();
		AnimatedGifTranscode.PrepareForGif(stored);
		await stored.SaveAsGifAsync(gif, new GifEncoder());

		gif.Position = 0;
		using var transcoded = await Image.LoadAsync<Rgba32>(gif);

		var opaque = new int[transcoded.Frames.Count];
		for (var index = 0; index < transcoded.Frames.Count; index++)
		{
			opaque[index] = Opaque(transcoded.Frames[index]);
		}

		Assert.That(opaque,
			Is.EqualTo(new[] { 64, 64, 64 }),
			"a frame carrying more than its own square is the frame before it showing through");
	}

	/// <summary>
	/// Asking a frame for its GIF metadata is what creates it, and what it creates carries no delay. So
	/// the timing has to be copied across at the same moment the disposal is set - left out, every frame
	/// asks to be shown for no time at all, which each reader answers with a floor of its own and the
	/// animation runs several times too slow.
	/// </summary>
	[Test]
	public async Task GifTranscodeKeepsTheTimingTheAnimationWasStoredWith()
	{
		using var source = ThreeMovingSquares();
		foreach (var frame in source.Frames)
		{
			var webpFrame = frame.Metadata.GetWebpMetadata();
			webpFrame.BlendMethod = WebpBlendMethod.Source;
			webpFrame.DisposalMethod = WebpDisposalMethod.DoNotDispose;
			webpFrame.FrameDelay = 30;
		}

		using var webp = new MemoryStream();
		await source.SaveAsync(webp, new WebpEncoder { FileFormat = WebpFileFormatType.Lossless });

		webp.Position = 0;
		using var stored = await Image.LoadAsync<Rgba32>(webp);
		using var gif = new MemoryStream();
		AnimatedGifTranscode.PrepareForGif(stored);
		await stored.SaveAsGifAsync(gif, new GifEncoder());

		gif.Position = 0;
		using var transcoded = await Image.LoadAsync<Rgba32>(gif);

		// 30ms is 3 hundredths of a second - the unit GIF counts in.
		Assert.That(transcoded.Frames.Select(f => f.Metadata.GetGifMetadata().FrameDelay),
			Is.All.EqualTo(3));
	}
}
