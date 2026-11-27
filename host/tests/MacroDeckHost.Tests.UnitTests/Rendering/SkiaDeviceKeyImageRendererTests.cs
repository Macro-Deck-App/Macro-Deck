using System.Text.Json;
using MacroDeck.Sdk.Layouts;
using MacroDeckHost.Application.Devices.Surfaces.KeyImages;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Infrastructure.Rendering;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using SkiaSharp;

namespace MacroDeckHost.Tests.UnitTests.Rendering;

[TestFixture]
internal sealed class SkiaDeviceKeyImageRendererTests
{
	[Test]
	public async Task A_key_arrives_as_a_jpeg_of_the_key_size_rotated_clockwise()
	{
		using var document = JsonDocument.Parse("""
												{"revision":1,"root":{"id":"r","type":"ui.stack","properties":{"direction":"horizontal"},"children":[
													{"id":"a","type":"ui.stack","properties":{"fill":true,"background":"#ff0000"}},
													{"id":"b","type":"ui.stack","properties":{"fill":true,"background":"#0000ff"}}]}}
												""");
		using var renderer = new SkiaDeviceKeyImageRenderer(new AntaCatalog(), TestLocalization.Resolver);

		var result = renderer.Render(new DeviceKeyImageRenderRequest
		{
			KeySize = new LayoutKeySize(80, 60),
			Format = new LayoutKeyImageFormat { Encoding = KeyImageEncodings.Jpeg, Rotation = 90 },
			Kind = DeviceKeyCellKind.Widget,
			WidgetType = "Clock",
			Tree = document.RootElement.Clone(),
			Appearance = new DeviceKeyImageAppearance("dark", null, AntaCatalog.Family),
			Culture = "en",
			TimeZone = TimeZoneInfo.Utc,
			Now = new DateTimeOffset(2026, 9, 12, 14, 5, 30, TimeSpan.Zero)
		});

		var key = result.Keys.Single();
		using var decoded = SKBitmap.Decode(key.Content.ToArray());
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(key.ContentType, Is.EqualTo(KeyImageContentTypes.Jpeg));
			Assert.That((decoded.Width, decoded.Height), Is.EqualTo((80, 60)));
			Assert.That(decoded.GetPixel(40, 10).Red, Is.GreaterThan(200));
			Assert.That(decoded.GetPixel(40, 10).Blue, Is.LessThan(60));
			Assert.That(decoded.GetPixel(40, 50).Blue, Is.GreaterThan(200));
			Assert.That(decoded.GetPixel(40, 50).Red, Is.LessThan(60));
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task The_same_key_drawn_on_several_threads_at_once_comes_out_identical()
	{
		using var document = JsonDocument.Parse("""
												{"revision":1,"root":{"id":"r","type":"ui.stack","children":[
													{"id":"t","type":"ui.text","properties":{"text":"Volume 音量 😀"}}]}}
												""");
		using var renderer = new SkiaDeviceKeyImageRenderer(new AntaCatalog(), TestLocalization.Resolver);
		var request = new DeviceKeyImageRenderRequest
		{
			KeySize = new LayoutKeySize(72, 72),
			Format = new LayoutKeyImageFormat { Encoding = KeyImageEncodings.Png },
			Kind = DeviceKeyCellKind.Widget,
			WidgetType = "Clock",
			Tree = document.RootElement.Clone(),
			Appearance = new DeviceKeyImageAppearance("dark", null, AntaCatalog.Family),
			Culture = "ja",
			TimeZone = TimeZoneInfo.Utc,
			Now = new DateTimeOffset(2026, 9, 12, 14, 5, 30, TimeSpan.Zero)
		};
		const int threads = 8;
		using var start = new Barrier(threads);

		var renders = await Task.WhenAll(Enumerable.Range(0, threads)
			.Select(_ => Task.Factory.StartNew(() =>
				{
					start.SignalAndWait();
					return Enumerable.Range(0, 3)
						.Select(_ => renderer.Render(request).Keys.Single().Content.ToArray())
						.ToList();
				},
				CancellationToken.None,
				TaskCreationOptions.LongRunning,
				TaskScheduler.Default)));

		var expected = renderer.Render(request).Keys.Single().Content.ToArray();
		Assert.That(renders.SelectMany(pictures => pictures), Has.All.EqualTo(expected));
	}

	[TestCase(240, 60, 25, true)]
	[TestCase(60, 30, 6, false)]
	[TestCase(60, 30, 15, true)]
	public async Task A_fixed_radius_rounds_the_key_by_that_many_of_its_pixels(
		int keySize,
		int cornerRadius,
		int diagonal,
		bool insideTheKey)
	{
		var format = new LayoutKeyImageFormat { Encoding = KeyImageEncodings.Png, CornerRadius = cornerRadius };
		var layout = DeviceKeyImageLayout.Of(new LayoutDescriptor("plugin::deck",
				"Deck",
				[
					new LayoutRegion
					{
						Id = "keys",
						Kind = LayoutRegionKinds.Grid,
						Grid = new LayoutGrid
						{
							Rows = 1, Columns = 1, KeySize = new LayoutKeySize(keySize, keySize), KeyImages = format
						}
					}
				]),
			out _)!;
		using var renderer = new SkiaDeviceKeyImageRenderer(new AntaCatalog(), TestLocalization.Resolver);

		var key = renderer.Render(new DeviceKeyImageRenderRequest
			{
				KeySize = layout.KeySize,
				Format = layout.Format,
				Kind = DeviceKeyCellKind.Empty,
				CornerRadius = layout.CornerRadiusFor(22, true),
				Appearance = new DeviceKeyImageAppearance("dark", null, AntaCatalog.Family),
				Culture = "en",
				TimeZone = TimeZoneInfo.Utc,
				Now = DateTimeOffset.UnixEpoch
			})
			.Keys
			.Single();
		using var decoded = SKBitmap.Decode(key.Content.ToArray());

		var expected = insideTheKey ? decoded.GetPixel(keySize / 2, keySize / 2) : decoded.GetPixel(0, 0);
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(decoded.GetPixel(0, 0), Is.Not.EqualTo(decoded.GetPixel(keySize / 2, keySize / 2)));
			Assert.That(decoded.GetPixel(diagonal, diagonal), Is.EqualTo(expected));
			await Task.CompletedTask;
		});
	}

	private sealed class AntaCatalog : IFontCatalog
	{
		public const string Family = "Anta";

		private static readonly FontFaceInfo _face = new("anta-400-5-upright",
			Family,
			400,
			5,
			FontFaceIdentity.UprightSlant,
			"Regular",
			true);

		public IReadOnlyList<FontFaceInfo> GetFaces()
		{
			return [_face];
		}

		public byte[]? GetFaceFile(string faceId)
		{
			return faceId == _face.FaceId
				? File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory,
					"Rendering",
					"Fixtures",
					"Anta-Regular.ttf"))
				: null;
		}
	}
}
