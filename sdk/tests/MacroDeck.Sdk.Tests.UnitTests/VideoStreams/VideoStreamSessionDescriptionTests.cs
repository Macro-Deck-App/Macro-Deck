using MacroDeck.Sdk.VideoStreams;

namespace MacroDeck.Sdk.Tests.UnitTests.VideoStreams;

[TestFixture]
internal sealed class VideoStreamSessionDescriptionTests
{
	[Test]
	public void Hls_and_Mjpeg_name_their_transport_and_keep_the_url()
	{
		var hls = VideoStreamSessionDescription.Hls("http://127.0.0.1:8080/live/index.m3u8?token=a%2Fb");
		var mjpeg = VideoStreamSessionDescription.Mjpeg("https://camera.local/stream.mjpg");

		Assert.Multiple(() =>
		{
			Assert.That(hls.Transport, Is.EqualTo("hls"));
			Assert.That(hls.Url, Is.EqualTo("http://127.0.0.1:8080/live/index.m3u8?token=a%2Fb"));
			Assert.That(mjpeg.Transport, Is.EqualTo("mjpeg"));
			Assert.That(mjpeg.Url, Is.EqualTo("https://camera.local/stream.mjpg"));
		});
	}

	[Test]
	public void FromUrl_accepts_any_well_formed_transport_token()
	{
		var description = VideoStreamSessionDescription.FromUrl("x-dash+http.v2", "http://127.0.0.1/a");

		Assert.That(description.Transport, Is.EqualTo("x-dash+http.v2"));
	}

	[TestCase("")]
	[TestCase("HLS")]
	[TestCase("h ls")]
	[TestCase("abcdefghijklmnopqrstuvwxyz0123456")]
	public void A_transport_that_is_not_a_lowercase_token_is_rejected(string transport)
		=> Assert.That(() => VideoStreamSessionDescription.FromUrl(transport, "http://127.0.0.1/a"),
			Throws.ArgumentException);

	[TestCase(null)]
	[TestCase("")]
	[TestCase("/live/index.m3u8")]
	[TestCase("camera.local/index.m3u8")]
	[TestCase("ftp://camera.local/index.m3u8")]
	[TestCase("file:///tmp/index.m3u8")]
	[TestCase("rtsp://camera.local/stream")]
	[TestCase("http:/camera.local/index.m3u8")]
	[TestCase("http://user:secret@camera.local/index.m3u8")]
	[TestCase("http://user@camera.local/index.m3u8")]
	[TestCase("http://@camera.local/index.m3u8")]
	[TestCase("http://camera.local/a%2Fb/index.m3u8")]
	[TestCase("http://camera.local/a%2fb.m3u8")]
	[TestCase("http://camera.local/a\\b.m3u8")]
	public void A_url_that_is_not_a_plain_absolute_http_url_is_rejected(string? url)
	{
		Assert.Multiple(() =>
		{
			Assert.That(() => VideoStreamSessionDescription.Hls(url!), Throws.ArgumentException);
			Assert.That(() => VideoStreamSessionDescription.Mjpeg(url!), Throws.ArgumentException);
		});
	}

	[Test]
	public void A_url_over_2048_characters_is_rejected_and_one_of_exactly_2048_is_accepted()
	{
		const string prefix = "http://127.0.0.1/";
		var atLimit = prefix + new string('a', 2048 - prefix.Length);

		Assert.Multiple(() =>
		{
			Assert.That(VideoStreamSessionDescription.Mjpeg(atLimit).Url, Has.Length.EqualTo(2048));
			Assert.That(() => VideoStreamSessionDescription.Mjpeg(atLimit + "a"), Throws.ArgumentException);
		});
	}

	[Test]
	public void An_encoded_slash_is_fine_in_the_query_where_it_is_data()
		=> Assert.That(VideoStreamSessionDescription.Hls("http://camera.local/index.m3u8?src=a%2Fb").Url,
			Does.EndWith("src=a%2Fb"));
}
