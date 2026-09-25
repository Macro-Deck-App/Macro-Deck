using MacroDeckHost.Application.Icons;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
public class IconFallbackStoreTests
{
	private IconTestHarness _harness = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new IconTestHarness();
	}

	[TearDown]
	public void TearDown()
	{
		_harness.Dispose();
	}

	[Test]
	public async Task An_animated_icon_falls_back_to_a_gif_that_still_moves()
	{
		var icon = await StoreAnimatedIcon(frameCount: 3);

		var image = await _harness.FallbackStore.GetOrCreate(icon,
			IconVariants.Master,
			staticFrame: false,
			CancellationToken.None);

		Assert.That(image, Is.Not.Null);
		using var decoded = await Image.LoadAsync(image!.Content);
		Assert.Multiple(() =>
		{
			Assert.That(image.ContentType, Is.EqualTo("image/gif"));
			Assert.That(decoded.Frames.Count, Is.EqualTo(3));
		});
	}

	[Test]
	public async Task An_animated_icon_asked_for_a_static_frame_yields_its_first_frame_as_png()
	{
		var icon = await StoreAnimatedIcon(frameCount: 3);

		var image = await _harness.FallbackStore.GetOrCreate(icon,
			IconVariants.Master,
			staticFrame: true,
			CancellationToken.None);

		Assert.That(image, Is.Not.Null);
		using var decoded = await Image.LoadAsync<Rgba32>(image!.Content);
		Assert.Multiple(() =>
		{
			Assert.That(image.ContentType, Is.EqualTo("image/png"));
			Assert.That(decoded.Frames.Count, Is.EqualTo(1));
			Assert.That(decoded[2, 2], Is.EqualTo(FrameColor(0)), "the still must be the animation's first frame");
		});
	}

	[Test]
	public async Task An_animated_icon_falls_back_to_a_gif_that_draws_exactly_its_mostly_opaque_pixels()
	{
		using var source = new Image<Rgba32>(EdgedFrameSize, EdgedFrameSize);
		PaintEdgedSquare(source.Frames.RootFrame, offset: 2);
		using (var second = new Image<Rgba32>(EdgedFrameSize, EdgedFrameSize))
		{
			PaintEdgedSquare(second.Frames.RootFrame, offset: 6);
			source.Frames.AddFrame(second.Frames.RootFrame);
		}

		var icon = await StoreAnimatedIcon(source);

		var image = await _harness.FallbackStore.GetOrCreate(icon,
			IconVariants.Master,
			staticFrame: false,
			CancellationToken.None);

		Assert.That(image, Is.Not.Null);
		using var decoded = await Image.LoadAsync<Rgba32>(image!.Content);
		Assert.That(decoded.Frames.Count, Is.EqualTo(2));
		for (var index = 0; index < decoded.Frames.Count; index++)
		{
			Assert.That(DrawnPixels(decoded.Frames[index], alphaThreshold: 128),
				Is.EquivalentTo(DrawnPixels(source.Frames[index], alphaThreshold: 128)),
				$"frame {index}");
		}
	}

	private const int EdgedFrameSize = 20;

	private static void PaintEdgedSquare(ImageFrame<Rgba32> frame, int offset)
	{
		for (var y = 0; y < EdgedFrameSize; y++)
		{
			for (var x = 0; x < EdgedFrameSize; x++)
			{
				var distance = Math.Max(Math.Max(offset - x, x - (offset + 5)), Math.Max(offset - y, y - (offset + 5)));
				var alpha = distance switch
				{
					<= 0 => 255,
					1 => 192,
					2 => 64,
					_ => 0
				};
				frame[x, y] = new Rgba32(255, 255, 255, (byte)alpha);
			}
		}
	}

	private static List<(int X, int Y)> DrawnPixels(ImageFrame<Rgba32> frame, int alphaThreshold)
	{
		var drawn = new List<(int X, int Y)>();
		for (var y = 0; y < frame.Height; y++)
		{
			for (var x = 0; x < frame.Width; x++)
			{
				if (frame[x, y].A >= alphaThreshold)
				{
					drawn.Add((x, y));
				}
			}
		}

		return drawn;
	}

	[Test]
	public async Task A_replaced_icon_leaves_only_its_current_fallback_image_behind()
	{
		var icon = await StoreAnimatedIcon(frameCount: 2);
		(await _harness.FallbackStore.GetOrCreate(icon, IconVariants.Master, staticFrame: false, CancellationToken.None))!
			.Content.Dispose();

		icon.MasterContentHash = "sha256:" + new string('b', 64);
		(await _harness.FallbackStore.GetOrCreate(icon, IconVariants.Master, staticFrame: false, CancellationToken.None))!
			.Content.Dispose();

		var cached = Directory.GetFiles(Path.Combine(_harness.Paths.IconsDirectory, "fallback-cache"), $"{icon.Id:N}-*");
		Assert.That(cached, Has.Length.EqualTo(1));
	}

	private async Task<IconEntity> StoreAnimatedIcon(int frameCount)
	{
		using var image = new Image<Rgba32>(8, 8, FrameColor(0));
		for (var i = 1; i < frameCount; i++)
		{
			using var frame = new Image<Rgba32>(8, 8, FrameColor(i));
			image.Frames.AddFrame(frame.Frames.RootFrame);
		}

		return await StoreAnimatedIcon(image);
	}

	private async Task<IconEntity> StoreAnimatedIcon(Image<Rgba32> image)
	{
		var packId = Guid.NewGuid();
		var icon = new IconEntity
		{
			Id = Guid.NewGuid(),
			PackId = packId,
			Name = "spinner",
			IsAnimated = true,
			FrameCount = image.Frames.Count,
			ProcessingState = IconProcessingState.Ready
		};

		foreach (var frame in image.Frames)
		{
			var webpFrame = frame.Metadata.GetWebpMetadata();
			webpFrame.FrameDelay = 100;
			webpFrame.BlendMethod = WebpBlendMethod.Source;
			webpFrame.DisposalMethod = WebpDisposalMethod.DoNotDispose;
		}

		using var stream = new MemoryStream();
		await image.SaveAsync(stream, new WebpEncoder { FileFormat = WebpFileFormatType.Lossless });
		await _harness.Storage.WriteVariant(packId,
			icon.Id,
			IconVariants.Master,
			stream.ToArray(),
			CancellationToken.None);
		return icon;
	}

	private static Rgba32 FrameColor(int index) => new((byte)(40 + index * 60), 20, 200, 255);
}
