using MacroDeckHost.Integrations.YtmDesktop;

namespace MacroDeckHost.Tests.UnitTests.YtmDesktop;

[TestFixture]
internal sealed class YtmDesktopUrlsTests
{
	[Test]
	public void TryParse_reads_video_and_playlist_from_a_music_youtube_watch_url()
	{
		var ok = YtmDesktopUrls.TryParse("https://music.youtube.com/watch?v=dQw4w9WgXcQ&list=PLabc123",
			out var videoId,
			out var playlistId);

		Assert.Multiple(() =>
		{
			Assert.That(ok, Is.True);
			Assert.That(videoId, Is.EqualTo("dQw4w9WgXcQ"));
			Assert.That(playlistId, Is.EqualTo("PLabc123"));
		});
	}

	[Test]
	public void TryParse_reads_video_only_from_a_www_youtube_watch_url()
	{
		var ok = YtmDesktopUrls.TryParse("https://www.youtube.com/watch?v=dQw4w9WgXcQ",
			out var videoId,
			out var playlistId);

		Assert.Multiple(() =>
		{
			Assert.That(ok, Is.True);
			Assert.That(videoId, Is.EqualTo("dQw4w9WgXcQ"));
			Assert.That(playlistId, Is.Null);
		});
	}

	[Test]
	public void TryParse_reads_video_and_playlist_from_a_youtu_be_short_link()
	{
		var ok = YtmDesktopUrls.TryParse("https://youtu.be/dQw4w9WgXcQ?list=PLabc123",
			out var videoId,
			out var playlistId);

		Assert.Multiple(() =>
		{
			Assert.That(ok, Is.True);
			Assert.That(videoId, Is.EqualTo("dQw4w9WgXcQ"));
			Assert.That(playlistId, Is.EqualTo("PLabc123"));
		});
	}

	[Test]
	public void TryParse_reads_playlist_only_from_a_playlist_url()
	{
		var ok = YtmDesktopUrls.TryParse("https://music.youtube.com/playlist?list=PLabc123",
			out var videoId,
			out var playlistId);

		Assert.Multiple(() =>
		{
			Assert.That(ok, Is.True);
			Assert.That(videoId, Is.Null);
			Assert.That(playlistId, Is.EqualTo("PLabc123"));
		});
	}

	[Test]
	public void TryParse_strips_the_vl_prefix_from_a_browse_playlist_url()
	{
		var ok = YtmDesktopUrls.TryParse("https://music.youtube.com/browse/VLPLabc",
			out var videoId,
			out var playlistId);

		Assert.Multiple(() =>
		{
			Assert.That(ok, Is.True);
			Assert.That(videoId, Is.Null);
			Assert.That(playlistId, Is.EqualTo("PLabc"));
		});
	}

	[Test]
	public void TryParse_treats_an_eleven_character_token_as_a_video_id()
	{
		var ok = YtmDesktopUrls.TryParse("dQw4w9WgXcQ", out var videoId, out var playlistId);

		Assert.Multiple(() =>
		{
			Assert.That(ok, Is.True);
			Assert.That(videoId, Is.EqualTo("dQw4w9WgXcQ"));
			Assert.That(playlistId, Is.Null);
		});
	}

	[TestCase("PLabc")]
	[TestCase("OLAK5uy_kExampleId123456")]
	[TestCase("RDCLAK5uy_kExampleId123")]
	[TestCase("LM")]
	public void TryParse_treats_a_known_playlist_prefix_as_a_playlist_id(string token)
	{
		var ok = YtmDesktopUrls.TryParse(token, out var videoId, out var playlistId);

		Assert.Multiple(() =>
		{
			Assert.That(ok, Is.True);
			Assert.That(videoId, Is.Null);
			Assert.That(playlistId, Is.EqualTo(token));
		});
	}

	[TestCase("abcdefghij")]
	[TestCase("abcdefghijkl")]
	public void TryParse_treats_a_token_of_the_wrong_length_as_a_playlist_id(string token)
	{
		var ok = YtmDesktopUrls.TryParse(token, out var videoId, out var playlistId);

		Assert.Multiple(() =>
		{
			Assert.That(ok, Is.True);
			Assert.That(videoId, Is.Null);
			Assert.That(playlistId, Is.EqualTo(token));
		});
	}

	[Test]
	public void TryParse_unescapes_a_percent_encoded_query_value()
	{
		var ok = YtmDesktopUrls.TryParse("https://music.youtube.com/watch?v=dQw4w9WgXcQ&list=My%20List",
			out _,
			out var playlistId);

		Assert.Multiple(() =>
		{
			Assert.That(ok, Is.True);
			Assert.That(playlistId, Is.EqualTo("My List"));
		});
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("   ")]
	public void TryParse_returns_false_for_null_empty_or_whitespace(string? value)
	{
		var ok = YtmDesktopUrls.TryParse(value, out var videoId, out var playlistId);

		Assert.Multiple(() =>
		{
			Assert.That(ok, Is.False);
			Assert.That(videoId, Is.Null);
			Assert.That(playlistId, Is.Null);
		});
	}
}
