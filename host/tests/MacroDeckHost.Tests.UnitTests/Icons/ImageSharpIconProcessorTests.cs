using System.Text;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Icons;
using Serilog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
public class ImageSharpIconProcessorTests
{
	private static readonly int[] _allVariantSizes = [128, 256, 512];

	private ImageSharpIconProcessor _processor = null!;

	[SetUp]
	public void SetUp()
	{
		_processor = new ImageSharpIconProcessor(new LoggerConfiguration().CreateLogger());
	}

	[Test]
	public async Task Process_Png_ProducesWebpMasterAndVariants()
	{
		var png = await CreatePng(600, 400);

		var result = await _processor.Process(new MemoryStream(png), "test.png", CancellationToken.None);

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		var data = result.Data!;
		Assert.Multiple(() =>
		{
			Assert.That(data.Width, Is.EqualTo(600));
			Assert.That(data.Height, Is.EqualTo(400));
			Assert.That(data.IsAnimated, Is.False);
			Assert.That(data.OriginalFormat, Is.EqualTo("PNG"));
			Assert.That(data.SourceContentHash.Value, Does.StartWith("sha256:"));
			Assert.That(data.MasterContentHash.Value, Does.StartWith("sha256:"));
			Assert.That(data.Variants.Keys, Is.EquivalentTo(_allVariantSizes));
		});

		using var master = Image.Load(data.MasterWebp);
		Assert.Multiple(() =>
		{
			Assert.That(master.Metadata.DecodedImageFormat?.Name, Is.EqualTo("Webp"));
			Assert.That(master.Width, Is.EqualTo(600));
		});

		using var variant = Image.Load(data.Variants[128]);
		Assert.That(Math.Max(variant.Width, variant.Height), Is.EqualTo(128));
	}

	[Test]
	public async Task Process_SmallPng_DoesNotUpscaleAndSkipsVariants()
	{
		var png = await CreatePng(64, 64);

		var result = await _processor.Process(new MemoryStream(png), "small.png", CancellationToken.None);

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Width, Is.EqualTo(64));
			Assert.That(result.Data!.Variants, Is.Empty);
		});
	}

	[Test]
	public async Task Process_LargePng_CapsMasterAt1024()
	{
		var png = await CreatePng(2048, 1024);

		var result = await _processor.Process(new MemoryStream(png), "large.png", CancellationToken.None);

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Width, Is.EqualTo(1024));
			Assert.That(result.Data!.Height, Is.EqualTo(512));
		});
	}

	[Test]
	public async Task Process_PngWithAlpha_PreservesTransparency()
	{
		var png = await CreatePng(200, 200, new Rgba32(255, 0, 0, 128));

		var result = await _processor.Process(new MemoryStream(png), "alpha.png", CancellationToken.None);

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		using var master = Image.Load<Rgba32>(result.Data!.MasterWebp);
		Assert.That(master[100, 100].A, Is.LessThan(255));
	}

	[Test]
	public async Task Process_AnimatedGif_ProducesAnimatedWebpWithDelays()
	{
		var gif = await CreateAnimatedGif(300, 300, frameCount: 3, frameDelayCentiseconds: 20);

		var result = await _processor.Process(new MemoryStream(gif), "animated.gif", CancellationToken.None);

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.IsAnimated, Is.True);
			Assert.That(result.Data!.FrameCount, Is.EqualTo(3));
			Assert.That(result.Data!.OriginalFormat, Is.EqualTo("GIF"));
		});

		using var master = Image.Load(result.Data!.MasterWebp);
		Assert.Multiple(() =>
		{
			Assert.That(master.Metadata.DecodedImageFormat?.Name, Is.EqualTo("Webp"));
			Assert.That(master.Frames.Count, Is.EqualTo(3));
			var delay = master.Frames[0].Metadata.GetWebpMetadata().FrameDelay;
			Assert.That(delay, Is.EqualTo(200), "frame delay should be 20cs = 200ms");
		});
	}

	[Test]
	public async Task Process_TransparentAnimatedGif_HasNoGhostTrailsAndKeepsExactColors()
	{
		using var gifImage = BuildFrame(100, 40, xStart: 0);
		using (var second = BuildFrame(100, 40, xStart: 80))
		{
			gifImage.Frames.AddFrame(second.Frames.RootFrame);
		}

		foreach (var frame in gifImage.Frames)
		{
			var metadata = frame.Metadata.GetGifMetadata();
			metadata.FrameDelay = 10;
			metadata.DisposalMethod = GifDisposalMethod.RestoreToBackground;
		}

		using var gifStream = new MemoryStream();
		await gifImage.SaveAsGifAsync(gifStream);

		var result = await _processor.Process(new MemoryStream(gifStream.ToArray()),
			"moving.gif",
			CancellationToken.None);

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		using var master = Image.Load<Rgba32>(result.Data!.MasterWebp);
		Assert.That(master.Frames.Count, Is.EqualTo(2));

		var secondFrame = master.Frames[1];
		Assert.Multiple(() =>
		{
			Assert.That(secondFrame[10, 20].A, Is.EqualTo(0), "ghost of frame 1 must not remain");
			Assert.That(secondFrame[90, 20].A, Is.EqualTo(255), "the drawn block must stay opaque");
			Assert.That(secondFrame[90, 20].R, Is.GreaterThanOrEqualTo(240), "the block must still read as red");
			Assert.That(secondFrame[90, 20].G, Is.LessThanOrEqualTo(15));
			Assert.That(secondFrame[90, 20].B, Is.LessThanOrEqualTo(15));
		});
	}

	[Test]
	public async Task Process_LongAnimatedGif_KeepsEveryRenditionSmallerThanItsMaster()
	{
		// A dithered animation is where a rendition can outgrow the master: resampling turns the
		// palette's flat patterns into noise, and a 256px rendition of a 400px 180-frame GIF once came
		// out at 11.7 MB, larger than the master it was cut from - and far too large for a deck to draw.
		var gif = await CreateDitheredAnimatedGif(400, 400, frameCount: 24);

		var result = await _processor.Process(new MemoryStream(gif), "long.gif", CancellationToken.None);

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		var master = result.Data!.MasterWebp.Length;
		Assert.Multiple(() =>
		{
			Assert.That(result.Data.FrameCount, Is.EqualTo(24));
			Assert.That(result.Data.Variants.Keys, Is.EquivalentTo(new[] { 128, 256 }));
			foreach (var (size, bytes) in result.Data.Variants)
			{
				Assert.That(bytes.Length, Is.LessThan(master), $"the {size}px rendition must not outgrow the master");
			}
		});
	}

	private static async Task<byte[]> CreateDitheredAnimatedGif(int width, int height, int frameCount)
	{
		// A GIF's palette is small, so a gradient in one is dithered: a two-colour checkerboard whose
		// pair shifts along the ramp. It is cheap to store as it is and expensive once resampled.
		var frames = new List<Image<Rgba32>>();
		for (var i = 0; i < frameCount; i++)
		{
			var frame = new Image<Rgba32>(width, height);
			for (var y = 0; y < height; y++)
			{
				for (var x = 0; x < width; x++)
				{
					var ramp = (x + y + i * 9) % 512;
					var step = (byte)(ramp / 32 * 32);
					var checker = (x + y) % 2 == 0;
					var shade = checker ? step : (byte)Math.Min(255, step + 32);
					frame[x, y] = new Rgba32(shade, (byte)(255 - shade), (byte)(shade / 2), 255);
				}
			}

			frame.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = 5;
			frames.Add(frame);
		}

		using var image = frames[0];
		for (var i = 1; i < frames.Count; i++)
		{
			image.Frames.AddFrame(frames[i].Frames.RootFrame);
			frames[i].Dispose();
		}

		using var stream = new MemoryStream();
		await image.SaveAsGifAsync(stream);
		return stream.ToArray();
	}

	private static Image<Rgba32> BuildFrame(int width, int height, int xStart)
	{
		var frame = new Image<Rgba32>(width, height);
		for (var y = 0; y < height; y++)
		{
			for (var x = xStart; x < xStart + 20 && x < width; x++)
			{
				frame[x, y] = new Rgba32(255, 0, 0, 255);
			}
		}

		return frame;
	}

	[Test]
	public async Task Process_GifWithFrameOverflowingCanvas_FallsBackToLenientDecoder()
	{
		var bytes = Convert.FromBase64String(
			"R0lGODlhCAAIAIEAAAAA/wAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQECgAAACwAAAAACAAIAAAIDwABCBxI" +
			"sKDBgwgTKkwYEAAh+QQFCgABACwAAAQACAAIAIH/AAAAAAAAAAAAAAAIDwABCBxIsKDBgwgTKkwYEAA7");

		var result = await _processor.Process(new MemoryStream(bytes), "overflow.gif", CancellationToken.None);

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.IsAnimated, Is.True);
			Assert.That(result.Data!.FrameCount, Is.EqualTo(2));
			Assert.That(result.Data!.OriginalFormat, Is.EqualTo("GIF"));
		});

		using var master = Image.Load(result.Data!.MasterWebp);
		Assert.That(master.Frames.Count, Is.EqualTo(2));
	}

	[Test]
	public async Task Process_Svg_RasterizesAtMasterSize()
	{
		var svg = """
				  <svg xmlns="http://www.w3.org/2000/svg" width="100" height="50" viewBox="0 0 100 50">
				  	<rect x="0" y="0" width="100" height="50" fill="#22c55e" fill-opacity="0.5"/>
				  </svg>
				  """;
		var bytes = Encoding.UTF8.GetBytes(svg);

		var result = await _processor.Process(new MemoryStream(bytes), "icon.svg", CancellationToken.None);

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.OriginalFormat, Is.EqualTo("Svg"));
			Assert.That(result.Data!.Width, Is.EqualTo(1024));
			Assert.That(result.Data!.Height, Is.EqualTo(512));
		});
	}

	[Test]
	public async Task Process_Jpeg_ProducesWebp()
	{
		using var image = new Image<Rgba32>(300, 200, new Rgba32(10, 20, 30));
		using var stream = new MemoryStream();
		await image.SaveAsJpegAsync(stream);

		var result = await _processor.Process(new MemoryStream(stream.ToArray()), "photo.jpg", CancellationToken.None);

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		Assert.That(result.Data!.OriginalFormat, Is.EqualTo("JPEG"));
	}

	[Test]
	public async Task Process_ExistingWebp_ReencodesSuccessfully()
	{
		using var image = new Image<Rgba32>(300, 200, new Rgba32(10, 20, 30));
		using var stream = new MemoryStream();
		await image.SaveAsWebpAsync(stream);

		var result = await _processor.Process(new MemoryStream(stream.ToArray()), "icon.webp", CancellationToken.None);

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		Assert.That(result.Data!.OriginalFormat, Is.EqualTo("Webp"));
	}

	[Test]
	public async Task Process_CorruptData_FailsWithUnsupportedFormat()
	{
		var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

		var result = await _processor.Process(new MemoryStream(bytes), "broken.png", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(IconError.UnsupportedFormat));
		});
	}

	[Test]
	public async Task Process_EmptyFile_Fails()
	{
		var result = await _processor.Process(new MemoryStream([]), "empty.png", CancellationToken.None);

		Assert.That(result.Success, Is.False);
	}

	[Test]
	public async Task Process_InvalidSvg_Fails()
	{
		var bytes = Encoding.UTF8.GetBytes("<svg this is not valid");

		var result = await _processor.Process(new MemoryStream(bytes), "broken.svg", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(IconError.UnsupportedFormat));
		});
	}

	private static async Task<byte[]> CreatePng(int width, int height, Rgba32? color = null)
	{
		using var image = new Image<Rgba32>(width, height, color ?? new Rgba32(0, 128, 255, 255));
		using var stream = new MemoryStream();
		await image.SaveAsPngAsync(stream);
		return stream.ToArray();
	}

	private static async Task<byte[]> CreateAnimatedGif(int width,
		int height,
		int frameCount,
		int frameDelayCentiseconds)
	{
		using var image = new Image<Rgba32>(width, height, new Rgba32(255, 0, 0));
		for (var i = 1; i < frameCount; i++)
		{
			using var frame = new Image<Rgba32>(width, height, new Rgba32((byte)(i * 60), 255, 0));
			image.Frames.AddFrame(frame.Frames.RootFrame);
		}

		foreach (var frame in image.Frames)
		{
			frame.Metadata.GetGifMetadata().FrameDelay = frameDelayCentiseconds;
		}

		using var stream = new MemoryStream();
		await image.SaveAsGifAsync(stream);
		return stream.ToArray();
	}
}
