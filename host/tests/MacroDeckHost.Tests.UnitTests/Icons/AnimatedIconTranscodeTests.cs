using MacroDeckHost.Infrastructure.Icons;
using SixLabors.ImageSharp;
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
	private const int ColourStep = 8;

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
				frame[index * 10 + x, y] = new Rgba32(255, 255, 255, 255);
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
		await AnimatedGifTranscode.SaveAsGifAsync(stored, gif, CancellationToken.None);

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
		await AnimatedGifTranscode.SaveAsGifAsync(stored, gif, CancellationToken.None);

		gif.Position = 0;
		using var transcoded = await Image.LoadAsync<Rgba32>(gif);

		Assert.That(transcoded.Frames.Select(f => f.Metadata.GetGifMetadata().FrameDelay), Is.All.EqualTo(3));
	}

	[TestCase(new[] { 33, 33, 33, 33, 33, 33, 33, 33, 33, 33, 33, 33 }, TestName = "30 fps")]
	[TestCase(new[] { 16, 17, 17, 16, 17, 17, 16, 17, 17, 16, 17, 17 }, TestName = "60 fps")]
	[TestCase(new[] { 14, 14, 14, 14, 14, 14, 14, 14 }, TestName = "70 fps")]
	[TestCase(new[] { 25, 25, 45, 45, 25, 45, 15, 35 }, TestName = "half way between two hundredths")]
	[TestCase(new[] { 20, 50, 100, 30, 200, 40, 70, 150, 20, 120, 16, 33, 0 }, TestName = "mixed")]
	[TestCase(new[] { 0, 10, 0, 1, 5 }, TestName = "10 ms or less")]
	public async Task GifTranscodeKeepsEveryFrameStartAndTheLoopLength(int[] delaysMs)
	{
		var shown = await TranscodedFrames(delaysMs);

		var effective = delaysMs.Select(delay => delay <= 10 ? 100 : delay).ToArray();
		var sourceStarts = effective.Select((_, index) => effective.Take(index).Sum()).ToArray();
		var last = delaysMs.Length - 1;
		var emittedStart = 0;
		Assert.Multiple(() =>
		{
			Assert.That(shown[^1].SourceIndex, Is.EqualTo(last), "a play ends on the file's last frame");
			foreach (var (sourceIndex, delayHundredths) in shown)
			{
				var earliest = sourceIndex == last ? Math.Max(5, 25 - effective[last]) : 5;
				Assert.That(emittedStart - sourceStarts[sourceIndex],
					Is.InRange(-earliest, 5),
					$"frame {sourceIndex} starts at {emittedStart} ms instead of {sourceStarts[sourceIndex]} ms");
				Assert.That(delayHundredths,
					Is.GreaterThanOrEqualTo(2),
					$"frame {sourceIndex} is short enough to be stretched");
				emittedStart += delayHundredths * 10;
			}

			Assert.That(Math.Abs(emittedStart - effective.Sum()), Is.LessThanOrEqualTo(5), "the loop keeps its length");
			Assert.That(shown.Select(frame => frame.SourceIndex), Is.Ordered.Ascending);
		});
	}

	[Test]
	public async Task GifTranscodeShowsAFrameOfTenMillisecondsOrLessForATenthOfASecond()
	{
		var shown = await TranscodedFrames([0, 10, 1, 40]);

		Assert.That(shown.Select(frame => frame.Delay), Is.EqualTo(new[] { 10, 10, 10, 4 }));
	}

	[Test]
	public async Task GifTranscodeKeepsAGifTimingUnchanged()
	{
		var shown = await TranscodedFrames([20, 30, 70, 100, 40]);

		Assert.That(shown.Select(frame => frame.Delay), Is.EqualTo(new[] { 2, 3, 7, 10, 4 }));
	}

	[Test]
	public async Task GifTranscodeEndsAPlayOnTheFilesLastFrame()
	{
		var shown = await TranscodedFrames([100, 100, 11]);

		Assert.That(shown, Is.EqualTo(new[] { (0, 10), (1, 9), (2, 2) }));
	}

	[TestCase(new[] { 11, 1000 }, new[] { 2, 99 })]
	[TestCase(new[] { 14, 500 }, new[] { 2, 49 })]
	[TestCase(new[] { 11, 11, 1000 }, new[] { 2, 100 })]
	public async Task GifTranscodeKeepsTheLoopOfAnAnimationWhoseFirstFramesAreTooShort(int[] delaysMs, int[] expected)
	{
		var shown = await TranscodedFrames(delaysMs);

		Assert.That(shown.Select(frame => frame.Delay), Is.EqualTo(expected));
	}

	[Test]
	public async Task GifTranscodeKeepsATooFastAnimationMoving()
	{
		var shown = await TranscodedFrames([11, 11]);

		Assert.That(shown,
			Is.EqualTo(new[] { (0, 2), (1, 2) }),
			"two frames of 22 ms cannot be written, a still would stop it");
	}

	[Test]
	public async Task GifTranscodeOfAnUndecodableGifShowsItsShortFramesForATenthOfASecond()
	{
		using var frames = SquaresOfTheirOwnColour(3);
		foreach (var frame in frames.Frames)
		{
			frame.Metadata.GetGifMetadata().FrameDelay = 0;
		}

		frames.Frames[1].Metadata.GetGifMetadata().FrameDelay = 1;
		frames.Frames[2].Metadata.GetGifMetadata().FrameDelay = 5;
		using var original = new MemoryStream();
		await frames.SaveAsGifAsync(original);

		using var decoded = SkiaAnimatedGifDecoder.TryDecode(original.ToArray());
		Assert.That(decoded, Is.Not.Null);
		using var gif = new MemoryStream();
		await AnimatedGifTranscode.SaveAsGifAsync(decoded!, gif, CancellationToken.None);

		gif.Position = 0;
		using var transcoded = await Image.LoadAsync<Rgba32>(gif);
		Assert.That(transcoded.Frames.Select(frame => frame.Metadata.GetGifMetadata().FrameDelay),
			Is.EqualTo(new[] { 10, 10, 5 }));
	}

	private static async Task<List<(int SourceIndex, int Delay)>> TranscodedFrames(int[] delaysMs)
	{
		using var source = SquaresOfTheirOwnColour(delaysMs.Length);
		for (var index = 0; index < delaysMs.Length; index++)
		{
			source.Frames[index].Metadata.GetWebpMetadata().FrameDelay = (uint)delaysMs[index];
		}

		using var gif = new MemoryStream();
		await AnimatedGifTranscode.SaveAsGifAsync(source, gif, CancellationToken.None);

		gif.Position = 0;
		using var transcoded = await Image.LoadAsync<Rgba32>(gif);
		return transcoded.Frames
			.Cast<ImageFrame<Rgba32>>()
			.Select(frame => (frame[4, 4].R / ColourStep, frame.Metadata.GetGifMetadata().FrameDelay))
			.ToList();
	}

	private static Image<Rgba32> SquaresOfTheirOwnColour(int count)
	{
		var image = new Image<Rgba32>(16, 16);
		PaintSquare(image.Frames.RootFrame, 0);
		for (var index = 1; index < count; index++)
		{
			using var next = new Image<Rgba32>(16, 16);
			PaintSquare(next.Frames.RootFrame, index);
			image.Frames.AddFrame(next.Frames.RootFrame);
		}

		return image;
	}

	private static void PaintSquare(ImageFrame<Rgba32> frame, int index)
	{
		for (var y = 0; y < 8; y++)
		{
			for (var x = 0; x < 8; x++)
			{
				frame[x, y] = new Rgba32((byte)(index * ColourStep), 255, 255, 255);
			}
		}
	}
}
