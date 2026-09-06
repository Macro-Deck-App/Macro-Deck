using MacroDeckHost.Infrastructure.MusicPlayer;
using Serilog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MacroDeckHost.Tests.UnitTests.MusicPlayer;

[TestFixture]
public class ImageSharpArtworkProcessorTests
{
	private static readonly int[] _allVariantSizes = [128, 256];

	private ImageSharpArtworkProcessor _processor = null!;

	[SetUp]
	public void SetUp()
	{
		_processor = new ImageSharpArtworkProcessor(new LoggerConfiguration().CreateLogger());
	}

	[Test]
	public async Task Process_Jpeg_ProducesWebpMasterAndVariants()
	{
		var jpeg = await CreateImage(600, 600, (image, stream) => image.SaveAsJpegAsync(stream));

		var result = await _processor.Process(jpeg, CancellationToken.None);

		Assert.That(result, Is.Not.Null);
		Assert.That(result.Variants.Keys, Is.EquivalentTo(_allVariantSizes));

		using var master = Image.Load(result.MasterWebp);
		Assert.Multiple(() =>
		{
			Assert.That(master.Metadata.DecodedImageFormat?.Name, Is.EqualTo("Webp"));
			Assert.That(master.Width, Is.EqualTo(600));
		});

		using var variant = Image.Load(result.Variants[128]);
		Assert.That(Math.Max(variant.Width, variant.Height), Is.EqualTo(128));
	}

	[Test]
	public async Task Process_SmallImage_DoesNotUpscaleAndSkipsVariants()
	{
		var png = await CreateImage(100, 100, (image, stream) => image.SaveAsPngAsync(stream));

		var result = await _processor.Process(png, CancellationToken.None);

		Assert.That(result, Is.Not.Null);
		Assert.That(result.Variants, Is.Empty);

		using var master = Image.Load(result.MasterWebp);
		Assert.That(master.Width, Is.EqualTo(100));
	}

	[Test]
	public async Task Process_LargeImage_CapsMasterAt640OnLongestEdge()
	{
		var png = await CreateImage(2000, 1000, (image, stream) => image.SaveAsPngAsync(stream));

		var result = await _processor.Process(png, CancellationToken.None);

		Assert.That(result, Is.Not.Null);
		using var master = Image.Load(result.MasterWebp);
		Assert.Multiple(() =>
		{
			Assert.That(master.Width, Is.EqualTo(640));
			Assert.That(master.Height, Is.EqualTo(320));
		});
	}

	[Test]
	public async Task Process_AnimatedGif_FlattensToSingleStaticFrame()
	{
		byte[] gif;
		using (var image = new Image<Rgba32>(300, 300, new Rgba32(255, 0, 0)))
		{
			using (var frame = new Image<Rgba32>(300, 300, new Rgba32(0, 255, 0)))
			{
				image.Frames.AddFrame(frame.Frames.RootFrame);
			}

			using var stream = new MemoryStream();
			await image.SaveAsGifAsync(stream);
			gif = stream.ToArray();
		}

		var result = await _processor.Process(gif, CancellationToken.None);

		Assert.That(result, Is.Not.Null);
		using var master = Image.Load(result.MasterWebp);
		Assert.That(master.Frames.Count, Is.EqualTo(1));
	}

	[Test]
	public async Task Process_CorruptData_ReturnsNull()
	{
		var result = await _processor.Process([1, 2, 3, 4, 5, 6, 7, 8], CancellationToken.None);

		Assert.That(result, Is.Null);
	}

	[Test]
	public async Task Process_EmptyData_ReturnsNull()
	{
		var result = await _processor.Process([], CancellationToken.None);

		Assert.That(result, Is.Null);
	}

	private static async Task<byte[]> CreateImage(int width,
		int height,
		Func<Image<Rgba32>, Stream, Task> save)
	{
		using var image = new Image<Rgba32>(width, height, new Rgba32(0, 128, 255, 255));
		using var stream = new MemoryStream();
		await save(image, stream);
		return stream.ToArray();
	}
}
