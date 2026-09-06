using MacroDeckHost.Infrastructure.Icons;
using Serilog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
public class AnimatedGifSizeMatrixTests
{
	private ImageSharpIconProcessor _processor = null!;

	[SetUp]
	public void SetUp()
	{
		_processor = new ImageSharpIconProcessor(new LoggerConfiguration().CreateLogger());
	}

	[TestCase(128, 128)]
	[TestCase(127, 127)]
	[TestCase(129, 129)]
	[TestCase(100, 128)]
	[TestCase(128, 100)]
	[TestCase(320, 128)]
	[TestCase(128, 320)]
	[TestCase(256, 256)]
	[TestCase(200, 150)]
	[TestCase(512, 512)]
	[TestCase(1500, 500)]
	public async Task Process_AnimatedGif_SucceedsForCanvasShape(int width, int height)
	{
		var gif = CreateAnimatedGif(width, height);

		var result = await _processor.Process(new MemoryStream(gif), "shape.gif", CancellationToken.None);

		Assert.That(result.Success, Is.True, $"{width}x{height}: {result.ErrorMessage}");
		using var master = Image.Load(result.Data!.MasterWebp);
		Assert.That(master.Frames.Count, Is.EqualTo(3), $"{width}x{height} must stay animated");
	}

	private static byte[] CreateAnimatedGif(int width, int height)
	{
		using var image = new Image<Rgba32>(width, height);
		FillFrame(image.Frames.RootFrame, 0, width, height);
		for (var i = 1; i < 3; i++)
		{
			using var frame = new Image<Rgba32>(width, height);
			FillFrame(frame.Frames.RootFrame, i, width, height);
			image.Frames.AddFrame(frame.Frames.RootFrame);
		}

		foreach (var frame in image.Frames)
		{
			var metadata = frame.Metadata.GetGifMetadata();
			metadata.FrameDelay = 10;
			metadata.DisposalMethod = GifDisposalMethod.RestoreToBackground;
		}

		using var stream = new MemoryStream();
		image.SaveAsGif(stream);
		return stream.ToArray();
	}

	private static void FillFrame(ImageFrame<Rgba32> frame, int index, int width, int height)
	{
		var blockWidth = Math.Max(1, width / 4);
		var xStart = Math.Min(index * blockWidth, width - blockWidth);
		for (var y = height / 4; y < height / 2; y++)
		{
			for (var x = xStart; x < xStart + blockWidth; x++)
			{
				frame[x, y] = new Rgba32(200, 40, 40, 255);
			}
		}
	}
}
