using System.IO.Compression;
using System.Text;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Icons;
using Serilog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
public class LottieIconProcessingTests
{
	private static readonly int[] _variantsBelowTheLottieMaster = [128, 256];

	private ImageSharpIconProcessor _processor = null!;

	[SetUp]
	public void SetUp()
	{
		_processor = new ImageSharpIconProcessor(new LoggerConfiguration().CreateLogger());
	}

	[Test]
	public async Task Process_LottieJson_ProducesAnimatedWebp()
	{
		var bytes = Encoding.UTF8.GetBytes(BuildLottie(durationFrames: 30));

		var result = await _processor.Process(new MemoryStream(bytes), "spinner.json", CancellationToken.None);

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		var data = result.Data!;
		Assert.Multiple(() =>
		{
			Assert.That(data.OriginalFormat, Is.EqualTo("Lottie"));
			Assert.That(data.IsAnimated, Is.True);
			Assert.That(data.FrameCount, Is.EqualTo(30));
			Assert.That(data.Width, Is.EqualTo(512));
			Assert.That(data.Height, Is.EqualTo(512));

			Assert.That(data.Variants.Keys, Is.EquivalentTo(_variantsBelowTheLottieMaster));
		});

		using var master = Image.Load(data.MasterWebp);
		Assert.Multiple(() =>
		{
			Assert.That(master.Metadata.DecodedImageFormat?.Name, Is.EqualTo("Webp"));
			Assert.That(master.Frames.Count, Is.EqualTo(30));
		});
	}

	[Test]
	public async Task Process_Lottie_KeepsFrameDelaysAndLoopsInEveryRendition()
	{
		var bytes = Encoding.UTF8.GetBytes(BuildLottie(durationFrames: 30));

		var result = await _processor.Process(new MemoryStream(bytes), "spinner.json", CancellationToken.None);

		Assert.That(result.Success, Is.True, result.ErrorMessage);

		foreach (var webp in result.Data!.Variants.Values.Prepend(result.Data!.MasterWebp))
		{
			using var image = Image.Load(webp);
			Assert.That(image.Metadata.GetWebpMetadata().RepeatCount, Is.EqualTo(0), "should loop forever");
			foreach (var frame in image.Frames)
			{
				var metadata = frame.Metadata.GetWebpMetadata();
				Assert.Multiple(() =>
				{
					Assert.That(metadata.FrameDelay, Is.EqualTo(33));
					Assert.That(metadata.BlendMethod, Is.EqualTo(WebpBlendMethod.Source));
				});
			}
		}
	}

	[Test]
	public async Task Process_Lottie_RendersTheAnimatedContent()
	{
		var bytes = Encoding.UTF8.GetBytes(BuildLottie(durationFrames: 30));

		var result = await _processor.Process(new MemoryStream(bytes), "spinner.json", CancellationToken.None);

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		using var master = Image.Load<Rgba32>(result.Data!.MasterWebp);

		var first = master.Frames[0];
		var last = master.Frames[^1];
		Assert.Multiple(() =>
		{
			Assert.That(first[64, 256].A, Is.GreaterThan(200), "square expected on the left in frame 0");
			Assert.That(last[64, 256].A, Is.LessThan(50), "left side expected to be empty in the last frame");
			Assert.That(last[440, 256].A, Is.GreaterThan(200), "square expected on the right in the last frame");
		});
	}

	[Test]
	public async Task Process_DotLottieArchive_ProducesAnimatedWebp()
	{
		var bytes = BuildDotLottie(BuildLottie(durationFrames: 15));

		var result = await _processor.Process(new MemoryStream(bytes), "spinner.lottie", CancellationToken.None);

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.OriginalFormat, Is.EqualTo("Lottie"));
			Assert.That(result.Data!.IsAnimated, Is.True);
			Assert.That(result.Data!.FrameCount, Is.EqualTo(15));
		});
	}

	[Test]
	public async Task Process_LottieWithByteOrderMark_ProducesAnimatedWebp()
	{
		var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
			.GetBytes(BuildLottie(durationFrames: 15));

		var result = await _processor.Process(new MemoryStream(bytes), "bom.json", CancellationToken.None);

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		Assert.That(result.Data!.FrameCount, Is.EqualTo(15));
	}

	[Test]
	public async Task Process_DotLottieWithAnImageLayer_RendersTheBundledAsset()
	{
		var image = CreateSolidPng(40, 40, new Rgba32(0, 0, 255, 255));
		var bytes = BuildDotLottie(BuildImageLayerLottie(),
			("images/img_0.png", image),
			("../evil.png", image));

		var result = await _processor.Process(new MemoryStream(bytes), "logo.lottie", CancellationToken.None);

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		Assert.That(File.Exists(Path.Combine(Path.GetTempPath(), "evil.png")), Is.False, "zip slip");

		using var master = Image.Load<Rgba32>(result.Data!.MasterWebp);
		var center = master.Frames[0][256, 256];
		Assert.Multiple(() =>
		{
			Assert.That(center.A, Is.GreaterThan(200), "the image layer should be visible");
			Assert.That(center.B, Is.GreaterThan(center.R), "the bundled asset is blue");
		});
	}

	[Test]
	public async Task Process_SingleFrameLottie_IsNotAnimated()
	{
		var bytes = Encoding.UTF8.GetBytes(BuildLottie(durationFrames: 1));

		var result = await _processor.Process(new MemoryStream(bytes), "still.json", CancellationToken.None);

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.IsAnimated, Is.False);
			Assert.That(result.Data!.FrameCount, Is.Null);
		});
	}

	[Test]
	public void LooksLikeLottie_DoesNotClaimOrdinaryArchives()
	{
		var zip = BuildDotLottie(BuildLottie(durationFrames: 5));

		Assert.Multiple(() =>
		{
			Assert.That(LottieAnimationRenderer.LooksLikeLottie("icons.zip", zip), Is.False);
			Assert.That(LottieAnimationRenderer.LooksLikeLottie("pack.streamDeckIconPack", zip), Is.False);
			Assert.That(LottieAnimationRenderer.LooksLikeLottie("spinner.lottie", zip), Is.True);
		});
	}

	[Test]
	public async Task Process_JsonThatIsNotLottie_Fails()
	{
		var bytes = Encoding.UTF8.GetBytes("""{"name":"not an animation","version":3}""");

		var result = await _processor.Process(new MemoryStream(bytes), "config.json", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(IconError.UnsupportedFormat));
		});
	}

	[Test]
	public async Task Process_LottieLongerThanTheFrameBudget_Fails()
	{
		var bytes = Encoding.UTF8.GetBytes(BuildLottie(durationFrames: 600));

		var result = await _processor.Process(new MemoryStream(bytes), "long.json", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(IconError.ValidationError));
			Assert.That(result.ErrorMessage, Does.Contain("longer than"));
		});
	}

	[Test]
	public async Task Process_LottieAuthoredAt60Fps_IsSampledAtThirty()
	{
		var bytes = Encoding.UTF8.GetBytes(BuildLottie(durationFrames: 120, frameRate: 60));

		var result = await _processor.Process(new MemoryStream(bytes), "fast.json", CancellationToken.None);

		Assert.That(result.Success, Is.True, result.ErrorMessage);

		Assert.That(result.Data!.FrameCount, Is.EqualTo(60));
	}

	private static string BuildLottie(int durationFrames, int frameRate = 30)
		=> $$"""
			 {
			 	"v": "5.7.4", "fr": {{frameRate}}, "ip": 0, "op": {{durationFrames}}, "w": 200, "h": 200,
			 	"nm": "test", "ddd": 0, "assets": [],
			 	"layers": [{
			 		"ddd": 0, "ind": 1, "ty": 4, "nm": "square", "sr": 1, "ao": 0, "bm": 0,
			 		"ip": 0, "op": {{durationFrames}}, "st": 0,
			 		"ks": {
			 			"o": { "a": 0, "k": 100 },
			 			"r": { "a": 0, "k": 0 },
			 			"p": { "a": 1, "k": [
			 				{ "t": 0, "s": [25, 100, 0], "e": [175, 100, 0],
			 				  "i": { "x": [1], "y": [1] }, "o": { "x": [0], "y": [0] } },
			 				{ "t": {{durationFrames}}, "s": [175, 100, 0] }
			 			] },
			 			"a": { "a": 0, "k": [0, 0, 0] },
			 			"s": { "a": 0, "k": [100, 100, 100] }
			 		},
			 		"shapes": [{ "ty": "gr", "nm": "group", "it": [
			 			{ "ty": "rc", "d": 1, "s": { "a": 0, "k": [40, 40] },
			 			  "p": { "a": 0, "k": [0, 0] }, "r": { "a": 0, "k": 0 } },
			 			{ "ty": "fl", "c": { "a": 0, "k": [1, 0, 0, 1] }, "o": { "a": 0, "k": 100 }, "r": 1 },
			 			{ "ty": "tr", "p": { "a": 0, "k": [0, 0] }, "a": { "a": 0, "k": [0, 0] },
			 			  "s": { "a": 0, "k": [100, 100] }, "r": { "a": 0, "k": 0 }, "o": { "a": 0, "k": 100 } }
			 		] }]
			 	}]
			 }
			 """;

	private static string BuildImageLayerLottie()
		=> """
		   {
		   	"v": "5.7.4", "fr": 30, "ip": 0, "op": 15, "w": 200, "h": 200, "nm": "asset", "ddd": 0,
		   	"assets": [{ "id": "image_0", "w": 40, "h": 40, "u": "images/", "p": "img_0.png", "e": 0 }],
		   	"layers": [{
		   		"ddd": 0, "ind": 1, "ty": 2, "nm": "logo", "refId": "image_0", "sr": 1, "ao": 0, "bm": 0,
		   		"ip": 0, "op": 15, "st": 0,
		   		"ks": {
		   			"o": { "a": 0, "k": 100 },
		   			"r": { "a": 0, "k": 0 },
		   			"p": { "a": 0, "k": [100, 100, 0] },
		   			"a": { "a": 0, "k": [20, 20, 0] },
		   			"s": { "a": 0, "k": [500, 500, 100] }
		   		}
		   	}]
		   }
		   """;

	private static byte[] CreateSolidPng(int width, int height, Rgba32 color)
	{
		using var image = new Image<Rgba32>(width, height, color);
		using var buffer = new MemoryStream();
		image.SaveAsPng(buffer);
		return buffer.ToArray();
	}

	private static byte[] BuildDotLottie(string animationJson, params (string Name, byte[] Content)[] assets)
	{
		using var buffer = new MemoryStream();
		using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
		{
			WriteEntry(archive,
				"manifest.json",
				"""{"version":"1.0","animations":[{"id":"spinner"}]}""");
			WriteEntry(archive, "animations/spinner.json", animationJson);
			foreach (var (name, content) in assets)
			{
				using var stream = archive.CreateEntry(name).Open();
				stream.Write(content);
			}
		}

		return buffer.ToArray();
	}

	private static void WriteEntry(ZipArchive archive, string name, string content)
	{
		using var stream = archive.CreateEntry(name).Open();
		using var writer = new StreamWriter(stream, Encoding.UTF8);
		writer.Write(content);
	}
}
