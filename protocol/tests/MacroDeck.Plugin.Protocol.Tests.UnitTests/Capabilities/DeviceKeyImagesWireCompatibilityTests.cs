using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Capabilities.DeviceProvider;
using MacroDeck.Plugin.Protocol.Capabilities.LayoutProvider;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Capabilities;

[TestFixture]
public class DeviceKeyImagesWireCompatibilityTests
{
	private static readonly byte[] _picture = [0, 128, 255, 255];

	[Test]
	public void A_grid_from_a_provider_that_predates_key_images_asks_for_none()
	{
		const string json = """{"rows":3,"columns":5,"keySize":{"width":72,"height":72}}""";

		var grid = JsonSerializer.Deserialize<LayoutGridDto>(json, PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(grid.Rows, Is.EqualTo(3));
			Assert.That(grid.KeySize, Is.EqualTo(new LayoutKeySizeDto { Width = 72, Height = 72 }));
			Assert.That(grid.KeyImages, Is.Null);
		});
	}

	[Test]
	public void A_key_image_format_from_a_provider_that_predates_the_corner_radius_keeps_the_profile_s()
	{
		const string json = """{"encoding":"jpeg","rotation":90}""";

		var format = JsonSerializer.Deserialize<LayoutKeyImageFormatDto>(json, PluginProtocolJson.Options)!;

		Assert.That(format, Is.EqualTo(new LayoutKeyImageFormatDto { Encoding = "jpeg", Rotation = 90 }));
	}

	[Test]
	public void A_peer_that_predates_the_corner_radius_still_reads_a_format_that_carries_it()
	{
		var format = new LayoutKeyImageFormatDto
		{
			Encoding = "png", Rotation = 180, MirrorHorizontal = true, CornerRadius = 12
		};
		var json = JsonSerializer.Serialize(format, PluginProtocolJson.Options);

		var asOldPeer = JsonSerializer.Deserialize<FormatBeforeCornerRadius>(json, PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain("\"cornerRadius\":12"));
			Assert.That(asOldPeer, Is.EqualTo(new FormatBeforeCornerRadius("png", 180, true)));
		});
	}

	[Test]
	public void A_session_open_from_a_host_that_predates_key_images_carries_none()
	{
		const string json = """{"sessionId":"s1","deviceId":"d1","providerDeviceId":"p1"}""";

		var open = JsonSerializer.Deserialize<DeviceSessionOpenArguments>(json, PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(open.SessionId, Is.EqualTo("s1"));
			Assert.That(open.KeyImages, Is.Null);
		});
	}

	[Test]
	public void A_provider_that_predates_key_images_still_reads_a_session_open_that_carries_them()
	{
		var open = new DeviceSessionOpenArguments
		{
			SessionId = "s1",
			DeviceId = "d1",
			ProviderDeviceId = "p1",
			KeyImages = new LayoutKeyImageFormatDto { Encoding = "jpeg", Rotation = 180 }
		};

		var asOldProvider = JsonSerializer.Deserialize<OpenBeforeKeyImages>(
			JsonSerializer.Serialize(open, PluginProtocolJson.Options),
			PluginProtocolJson.Options)!;

		Assert.That(asOldProvider, Is.EqualTo(new OpenBeforeKeyImages("s1", "d1", "p1")));
	}

	[Test]
	public void A_key_image_format_round_trips_on_the_grid_and_the_session_open()
	{
		var format = new LayoutKeyImageFormatDto
		{
			Encoding = "bmp", Rotation = 270, MirrorHorizontal = true, CornerRadius = 10
		};

		var grid = RoundTrip(new LayoutGridDto { Rows = 2, Columns = 4, KeyImages = format });
		var open = RoundTrip(new DeviceSessionOpenArguments
		{
			SessionId = "s1", DeviceId = "d1", ProviderDeviceId = "p1", KeyImages = format
		});

		Assert.Multiple(() =>
		{
			Assert.That(grid.KeyImages, Is.EqualTo(format));
			Assert.That(open.KeyImages, Is.EqualTo(format));
		});
	}

	[Test]
	public void Pictures_round_trip_inline_as_base64_or_by_transfer_id()
	{
		var arguments = new DeviceSessionKeyImagesArguments
		{
			SessionId = "s1",
			Images =
			[
				new DeviceSessionKeyImageDto
				{
					Row = 0,
					Column = 1,
					Revision = 4,
					ContentType = "application/x-macrodeck-rgba",
					Content = _picture
				},
				new DeviceSessionKeyImageDto
				{
					Row = 2,
					Column = 0,
					Revision = 1,
					ContentType = "image/png",
					TransferId = "t1"
				}
			]
		};

		var json = JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options);
		var actual = json.Deserialize<DeviceSessionKeyImagesArguments>(PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(json.GetProperty("images")[0].GetProperty("content").GetString(),
				Is.EqualTo(Convert.ToBase64String(_picture)));
			Assert.That(actual.Images[0].Content, Is.EqualTo(_picture));
			Assert.That(actual.Images[0] with { Content = null },
				Is.EqualTo(arguments.Images[0] with { Content = null }));
			Assert.That(actual.Images[1], Is.EqualTo(arguments.Images[1]));
		});
	}

	[Test]
	public void A_key_image_answer_round_trips()
	{
		var result = new DevicesKeyImageResult
		{
			Row = 1,
			Column = 2,
			Revision = 9,
			ContentType = "image/jpeg",
			TransferId = "t2"
		};

		Assert.That(RoundTrip(result), Is.EqualTo(result));
	}

	private static T RoundTrip<T>(T value)
	{
		return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, PluginProtocolJson.Options),
			PluginProtocolJson.Options)!;
	}

	private sealed record OpenBeforeKeyImages(string SessionId, string DeviceId, string ProviderDeviceId);

	private sealed record FormatBeforeCornerRadius(string Encoding, int Rotation, bool MirrorHorizontal);
}
