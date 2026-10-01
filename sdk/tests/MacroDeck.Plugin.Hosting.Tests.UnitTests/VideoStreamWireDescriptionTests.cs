using MacroDeck.Plugin.Hosting.Capabilities.VideoStreamProvider;
using MacroDeck.Plugin.Protocol.Capabilities.VideoStreamProvider;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.VideoStreams;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class VideoStreamWireDescriptionTests
{
	private static readonly string[] _urls =
	[
		"http://127.0.0.1:8080/live/index.m3u8",
		"https://camera.local/stream.mjpg?token=a%2Fb",
		"/relative/index.m3u8",
		"ftp://camera.local/index.m3u8",
		"file:///tmp/index.m3u8",
		"http://user:secret@camera.local/index.m3u8",
		"http://@camera.local/index.m3u8",
		"http://camera.local/a%2Fb/index.m3u8",
		"http://camera.local/a%2fb.m3u8",
		"http://camera.local/a\\b.m3u8",
		"http://127.0.0.1/" + new string('a', VideoStreamLimits.MaxUrlLength - 17),
		"http://127.0.0.1/" + new string('a', VideoStreamLimits.MaxUrlLength - 16),
		""
	];

	private static VideoStreamSessionDescriptionDto Dto(string transport, string? url)
		=> new() { Transport = transport, Url = url };

	[Test]
	public void A_description_the_sdk_builds_is_structurally_valid_and_round_trips()
	{
		var description = VideoStreamSessionDescription.Mjpeg("http://127.0.0.1:8080/cam.mjpg");

		var dto = VideoStreamWire.ToDto(description);
		var back = VideoStreamWire.ToDescription(dto);

		Assert.Multiple(() =>
		{
			Assert.That(VideoStreamWire.ValidateDescription(dto), Is.Null);
			Assert.That(dto.Transport, Is.EqualTo("mjpeg"));
			Assert.That(dto.Url, Is.EqualTo("http://127.0.0.1:8080/cam.mjpg"));
			Assert.That((back.Transport, back.Url), Is.EqualTo((description.Transport, description.Url)));
		});
	}

	[Test]
	public void The_wire_check_and_the_sdk_factory_agree_on_every_url()
	{
		foreach (var url in _urls)
		{
			var factoryAccepts = Accepts(() => VideoStreamSessionDescription.Hls(url));
			var wireAccepts = VideoStreamWire.ValidateDescription(Dto("hls", url)) is null;

			Assert.That(wireAccepts, Is.EqualTo(factoryAccepts), url.Length > 80 ? $"a url of {url.Length} characters" : url);
		}
	}

	[Test]
	public void The_wire_check_and_the_sdk_factory_agree_on_every_transport_token()
	{
		foreach (var transport in new[] { "hls", "x-dash+http.v2", "HLS", "", "h ls", new string('a', 32), new string('a', 33) })
		{
			var factoryAccepts = Accepts(() => VideoStreamSessionDescription.FromUrl(transport, "http://127.0.0.1/a"));
			var wireAccepts = VideoStreamWire.ValidateDescription(Dto(transport, "http://127.0.0.1/a")) is null;

			Assert.That(wireAccepts, Is.EqualTo(factoryAccepts), transport);
		}
	}

	[TestCase("hls")]
	[TestCase("mjpeg")]
	public void A_url_is_required_for_the_transports_that_play_from_one(string transport)
		=> Assert.That(VideoStreamWire.ValidateDescription(Dto(transport, null)), Is.Not.Null);

	[Test]
	public void Whether_the_host_plays_a_transport_is_not_decided_by_the_structural_check()
	{
		Assert.Multiple(() =>
		{
			Assert.That(VideoStreamWire.ValidateDescription(Dto("webrtc", "http://127.0.0.1/a")), Is.Null);
			Assert.That(VideoStreamWire.ValidateDescription(Dto("some-future-source", null)), Is.Null);
		});
	}

	[Test]
	public void A_missing_description_is_not_valid()
		=> Assert.That(VideoStreamWire.ValidateDescription(null), Is.Not.Null);

	private static bool Accepts(Func<VideoStreamSessionDescription> create)
	{
		try
		{
			create();
			return true;
		}
		catch (ArgumentException)
		{
			return false;
		}
	}
}
