using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Infrastructure.Icons;
using Serilog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
public class AnimatedGifLoopCountTests
{
	private const int Edge = 300;

	private IconTestHarness _harness = null!;
	private IconService _service = null!;
	private ImageSharpIconProcessor _processor = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new IconTestHarness();
		_service = new IconService(_harness.Cache,
			_harness.Storage,
			_harness.FallbackStore,
			_harness.VariantDeriver,
			_harness.Coalescer,
			_harness.Mediator,
			new IconPackOwnerRegistry([]));
		_processor = new ImageSharpIconProcessor(new LoggerConfiguration().CreateLogger());
	}

	[TearDown]
	public void TearDown()
	{
		_harness.Dispose();
	}

	[TestCase(null, 1)]
	[TestCase(0, 0)]
	[TestCase(1, 2)]
	[TestCase(3, 4)]
	public async Task An_imported_gif_is_stored_with_the_number_of_plays_it_asked_for(int? loop, int plays)
	{
		var result = await _processor.Process(new MemoryStream(Gif(loop)), "loop.gif", CancellationToken.None);

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		using var master = Image.Load(result.Data!.MasterWebp);
		Assert.That(master.Metadata.GetWebpMetadata().RepeatCount, Is.EqualTo(plays));
	}

	[TestCase(null)]
	[TestCase(0)]
	[TestCase(1)]
	[TestCase(3)]
	public void The_fallback_gif_decoder_reports_the_plays_the_gif_asked_for(int? loop)
	{
		var plays = loop switch { null => 1, 0 => 0, _ => loop.Value + 1 };

		using var decoded = SkiaAnimatedGifDecoder.TryDecode(Gif(loop));

		Assert.That(decoded, Is.Not.Null);
		Assert.That(decoded!.Metadata.GetWebpMetadata().RepeatCount, Is.EqualTo(plays));
	}

	[TestCase(null, null)]
	[TestCase(null, 128)]
	[TestCase(0, null)]
	[TestCase(0, 128)]
	[TestCase(1, null)]
	[TestCase(1, 128)]
	[TestCase(3, null)]
	[TestCase(3, 128)]
	public async Task A_served_gif_repeats_as_often_as_the_gif_that_was_imported(int? loop, int? size)
	{
		var icon = await ImportedIcon(Gif(loop));

		var served = await Serve(icon, size);

		Assert.That(LoopOf(served), Is.EqualTo(loop));
	}

	[Test]
	public async Task A_gif_cached_before_the_loop_fix_is_not_served_again()
	{
		var icon = await ImportedIcon(Gif(loop: 1));
		var directory = Path.Combine(_harness.Paths.IconsDirectory, "fallback-cache");
		Directory.CreateDirectory(directory);
		var staleName = $"{icon.Id:N}-{IconVariants.MasterToken(icon.MasterContentHash!)}-{IconVariants.Master}.gif";
		await File.WriteAllBytesAsync(Path.Combine(directory, staleName), Gif(loop: null));

		var served = await Serve(icon, size: null);

		Assert.That(LoopOf(served), Is.EqualTo(1));
	}

	private async Task<IconEntity> ImportedIcon(byte[] gif)
	{
		var processed = await _processor.Process(new MemoryStream(gif), "loop.gif", CancellationToken.None);
		Assert.That(processed.Success, Is.True, processed.ErrorMessage);

		var pack = await _harness.CreatePack();
		var icon = await _harness.AddReadyIcon(pack.Id, "loop", processed.Data!.MasterWebp);
		icon.IsAnimated = true;
		icon.FrameCount = processed.Data.FrameCount;
		await _harness.Cache.UpdateIcon(icon);
		return icon;
	}

	private async Task<byte[]> Serve(IconEntity icon, int? size)
	{
		var result = await _service.GetImage(icon.Id, size, acceptWebp: false, staticFrame: false, CancellationToken.None);

		Assert.That(result.Success, Is.True);
		await using var content = result.Data!.Content;
		using var buffer = new MemoryStream();
		await content.CopyToAsync(buffer);
		Assert.That(result.Data.ContentType, Is.EqualTo("image/gif"));
		return buffer.ToArray();
	}

	private static byte[] Gif(int? loop)
	{
		using var image = new Image<Rgba32>(Edge, Edge, Color(0));
		for (var index = 1; index < 3; index++)
		{
			using var frame = new Image<Rgba32>(Edge, Edge, Color(index));
			image.Frames.AddFrame(frame.Frames.RootFrame);
		}

		foreach (var frame in image.Frames)
		{
			frame.Metadata.GetGifMetadata().FrameDelay = 10;
		}

		image.Metadata.GetGifMetadata().RepeatCount = 0;
		using var stream = new MemoryStream();
		image.SaveAsGif(stream);
		var gif = stream.ToArray();

		var name = gif.AsSpan().IndexOf("NETSCAPE2.0"u8);
		Assert.That(name, Is.GreaterThan(0), "the fixture needs a loop block to rewrite");
		var block = name - 3;
		if (loop is null)
		{
			return [.. gif[..block], .. gif[(block + 19)..]];
		}

		gif[name + 13] = (byte)loop.Value;
		gif[name + 14] = (byte)(loop.Value >> 8);
		return gif;
	}

	private static int? LoopOf(byte[] gif)
	{
		var name = gif.AsSpan().IndexOf("NETSCAPE2.0"u8);
		return name < 0 ? null : gif[name + 13] | (gif[name + 14] << 8);
	}

	private static Rgba32 Color(int index) => new((byte)(40 + index * 80), 20, 200, 255);
}
