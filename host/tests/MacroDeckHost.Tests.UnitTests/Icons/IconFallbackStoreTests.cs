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

	private async Task<IconEntity> StoreAnimatedIcon(int frameCount)
	{
		var packId = Guid.NewGuid();
		var icon = new IconEntity
		{
			Id = Guid.NewGuid(),
			PackId = packId,
			Name = "spinner",
			IsAnimated = true,
			FrameCount = frameCount,
			ProcessingState = IconProcessingState.Ready
		};

		using var image = new Image<Rgba32>(8, 8, FrameColor(0));
		for (var i = 1; i < frameCount; i++)
		{
			using var frame = new Image<Rgba32>(8, 8, FrameColor(i));
			image.Frames.AddFrame(frame.Frames.RootFrame);
		}

		foreach (var frame in image.Frames)
		{
			frame.Metadata.GetWebpMetadata().FrameDelay = 100;
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
