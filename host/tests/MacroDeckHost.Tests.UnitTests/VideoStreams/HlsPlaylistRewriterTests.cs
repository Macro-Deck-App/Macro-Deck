using System.Text.RegularExpressions;
using MacroDeckHost.Application.VideoStreams;

namespace MacroDeckHost.Tests.UnitTests.VideoStreams;

[TestFixture]
internal sealed class HlsPlaylistRewriterTests
{
	private const string Token = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCde";
	private const string Relay = "/api/video-streams/relay/" + Token;

	private static readonly Uri _pinned = new("http://127.0.0.1:9000");
	private static readonly Uri _playlistUrl = new("http://127.0.0.1:9000/live/cam/index.m3u8?session=1");

	private static string Rewrite(string playlist, Uri? playlistUrl = null)
	{
		Assert.That(HlsPlaylistRewriter.TryRewrite(playlist, Token, _pinned, playlistUrl ?? _playlistUrl, out var rewritten),
			Is.True,
			"the playlist should have been accepted");
		return rewritten;
	}

	private static bool Accepts(string playlist, Uri? playlistUrl = null)
		=> HlsPlaylistRewriter.TryRewrite(playlist, Token, _pinned, playlistUrl ?? _playlistUrl, out _);

	[Test]
	public void A_master_playlist_sends_every_variant_and_rendition_through_the_relay()
	{
		var rewritten = Rewrite("""
			#EXTM3U
			#EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360,AUDIO="aud"
			low/index.m3u8
			#EXT-X-STREAM-INF:BANDWIDTH=2000000,RESOLUTION=1280x720,AUDIO="aud"
			http://127.0.0.1:9000/live/cam/high/index.m3u8?token=abc
			#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="aud",NAME="English",URI="/live/audio/en.m3u8"
			#EXT-X-I-FRAME-STREAM-INF:BANDWIDTH=90000,URI="iframes.m3u8"

			""");

		Assert.That(rewritten, Is.EqualTo($"""
			#EXTM3U
			#EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360,AUDIO="aud"
			{Relay}/live/cam/low/index.m3u8
			#EXT-X-STREAM-INF:BANDWIDTH=2000000,RESOLUTION=1280x720,AUDIO="aud"
			{Relay}/live/cam/high/index.m3u8?token=abc
			#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="aud",NAME="English",URI="{Relay}/live/audio/en.m3u8"
			#EXT-X-I-FRAME-STREAM-INF:BANDWIDTH=90000,URI="{Relay}/live/cam/iframes.m3u8"

			"""));
	}

	[Test]
	public void A_fragmented_mp4_playlist_rewrites_the_map_the_key_and_the_segments_but_leaves_other_schemes_alone()
	{
		var rewritten = Rewrite("""
			#EXTM3U
			#EXT-X-VERSION:7
			#EXT-X-TARGETDURATION:6
			#EXT-X-MAP:URI="init.mp4",BYTERANGE="720@0"
			#EXT-X-KEY:METHOD=AES-128,URI="http://127.0.0.1:9000/keys/1.bin",IV=0x1
			#EXT-X-SESSION-KEY:METHOD=SAMPLE-AES,URI="skd://license/1",KEYFORMAT="com.apple.streamingkeydelivery"
			#EXT-X-KEY:METHOD=SAMPLE-AES,URI="data:text/plain;base64,QUJD"
			#EXTINF:6.0,a title with URI="http://elsewhere.example/x" in it
			seg1.m4s
			#EXT-X-ENDLIST
			""");

		Assert.That(rewritten, Is.EqualTo($"""
			#EXTM3U
			#EXT-X-VERSION:7
			#EXT-X-TARGETDURATION:6
			#EXT-X-MAP:URI="{Relay}/live/cam/init.mp4",BYTERANGE="720@0"
			#EXT-X-KEY:METHOD=AES-128,URI="{Relay}/keys/1.bin",IV=0x1
			#EXT-X-SESSION-KEY:METHOD=SAMPLE-AES,URI="skd://license/1",KEYFORMAT="com.apple.streamingkeydelivery"
			#EXT-X-KEY:METHOD=SAMPLE-AES,URI="data:text/plain;base64,QUJD"
			#EXTINF:6.0,a title with URI="http://elsewhere.example/x" in it
			{Relay}/live/cam/seg1.m4s
			#EXT-X-ENDLIST
			"""));
	}

	[Test]
	public void A_uri_on_another_scheme_of_the_same_host_is_another_origin()
	{
		Assert.That(Accepts("#EXTM3U\n#EXT-X-KEY:METHOD=AES-128,URI=\"https://127.0.0.1:9000/key\"\n"), Is.False);
	}

	[Test]
	public void Every_reference_form_resolves_against_the_playlist_url_and_ends_as_an_absolute_relay_path()
	{
		var rewritten = Rewrite("""
			#EXTM3U
			seg/1.ts
			./seg/2.ts
			../up/3.ts
			../../../../way/up/4.ts
			/root/5.ts
			//127.0.0.1:9000/network-path/6.ts
			7.ts?a=1&b=two%20words
			8%20with%20space.ts
			#EXT-X-ENDLIST
			""");

		Assert.That(rewritten.Split('\n').Where(line => !line.StartsWith('#')), Is.EqualTo(new[]
		{
			$"{Relay}/live/cam/seg/1.ts",
			$"{Relay}/live/cam/seg/2.ts",
			$"{Relay}/live/up/3.ts",
			$"{Relay}/way/up/4.ts",
			$"{Relay}/root/5.ts",
			$"{Relay}/network-path/6.ts",
			$"{Relay}/live/cam/7.ts?a=1&b=two%20words",
			$"{Relay}/live/cam/8%20with%20space.ts"
		}));
	}

	[Test]
	public void A_relative_reference_resolves_against_the_final_url_after_a_redirect()
	{
		var effective = new Uri("http://127.0.0.1:9000/moved/elsewhere/index.m3u8");

		var rewritten = Rewrite("#EXTM3U\nseg.ts\n", effective);

		Assert.That(rewritten, Is.EqualTo($"#EXTM3U\n{Relay}/moved/elsewhere/seg.ts\n"));
	}

	[TestCase("http://127.0.0.1:9001/seg.ts", TestName = "Another_port_fails_the_playlist")]
	[TestCase("http://localhost:9000/seg.ts", TestName = "localhost_is_not_127_0_0_1")]
	[TestCase("http://camera.example/seg.ts", TestName = "Another_host_fails_the_playlist")]
	[TestCase("//camera.example/seg.ts", TestName = "A_network_path_reference_to_another_host_fails_the_playlist")]
	[TestCase("https://127.0.0.1:9000/seg.ts", TestName = "Another_scheme_fails_the_playlist")]
	public void A_segment_outside_the_pinned_origin_fails_the_whole_playlist(string uri)
	{
		Assert.Multiple(() =>
		{
			Assert.That(Accepts($"#EXTM3U\nok.ts\n{uri}\n"), Is.False, "as a segment line");
			Assert.That(Accepts($"#EXTM3U\n#EXT-X-MAP:URI=\"{uri}\"\nok.ts\n"), Is.False, "as a MAP attribute");
		});
	}

	[TestCase("file:///etc/passwd")]
	[TestCase("javascript:alert(1)")]
	[TestCase("content://media/external/1")]
	[TestCase("ftp://127.0.0.1/seg.ts")]
	public void A_uri_with_any_scheme_other_than_http_data_or_skd_fails_the_whole_playlist(string uri)
	{
		Assert.Multiple(() =>
		{
			Assert.That(Accepts($"#EXTM3U\nok.ts\n{uri}\n"), Is.False, "as a segment line");
			Assert.That(Accepts($"#EXTM3U\n#EXT-X-KEY:METHOD=AES-128,URI=\"{uri}\"\nok.ts\n"), Is.False, "as a KEY attribute");
		});
	}

	[TestCase("data:text/plain;base64,QUJD")]
	[TestCase("skd://license/1")]
	public void A_data_or_skd_uri_passes_through_untouched(string uri)
	{
		Assert.That(Rewrite($"#EXTM3U\n#EXT-X-KEY:METHOD=SAMPLE-AES,URI=\"{uri}\"\n"),
			Is.EqualTo($"#EXTM3U\n#EXT-X-KEY:METHOD=SAMPLE-AES,URI=\"{uri}\"\n"));
	}

	[Test]
	public void The_host_and_the_default_port_are_compared_the_way_a_browser_would()
	{
		var pinned = new Uri("https://Camera.Example");
		var url = new Uri("https://camera.example/live/index.m3u8");

		Assert.That(HlsPlaylistRewriter.TryRewrite("#EXTM3U\nhttps://CAMERA.example:443/a.ts\n", Token, pinned, url, out var rewritten), Is.True);
		Assert.That(rewritten, Is.EqualTo($"#EXTM3U\n{Relay}/a.ts\n"));
	}

	[Test]
	public void A_byte_order_mark_and_windows_line_endings_survive()
	{
		var rewritten = Rewrite("﻿#EXTM3U\r\n#EXT-X-MAP:URI=\"init.mp4\"\r\nseg.ts\r\n");

		Assert.That(rewritten, Is.EqualTo($"﻿#EXTM3U\r\n#EXT-X-MAP:URI=\"{Relay}/live/cam/init.mp4\"\r\n{Relay}/live/cam/seg.ts\r\n"));
	}

	[Test]
	public void Nothing_relative_is_left_for_the_client_to_resolve()
	{
		var rewritten = Rewrite("""
			#EXTM3U
			#EXT-X-MAP:URI="init.mp4"
			#EXT-X-PART:DURATION=0.5,URI="part1.m4s"
			#EXT-X-PRELOAD-HINT:TYPE=PART,URI="part2.m4s"
			#EXT-X-RENDITION-REPORT:URI="../other/index.m3u8",LAST-MSN=3
			#EXTINF:4.0,
			../seg.m4s
			""");

		var uris = new List<string>();
		foreach (var line in rewritten.Split('\n'))
		{
			if (line.StartsWith('#'))
			{
				uris.AddRange(Regex.Matches(line, "URI=\"([^\"]*)\"").Select(m => m.Groups[1].Value));
			}
			else if (line.Length > 0)
			{
				uris.Add(line);
			}
		}

		Assert.That(uris, Is.Not.Empty.And.All.StartWith(Relay + "/"));
	}

	[TestCase("#EXT-X-VERSION:3\nseg.ts\n", TestName = "A_playlist_must_start_with_the_extm3u_tag")]
	[TestCase("", TestName = "An_empty_body_is_not_a_playlist")]
	[TestCase("<html>#EXTM3U</html>\n", TestName = "A_page_that_mentions_the_tag_is_not_a_playlist")]
	[TestCase("#EXTM3U\n#EXT-X-KEY:METHOD=AES-128,URI=unquoted.key\n", TestName = "A_URI_attribute_that_is_not_quoted_fails")]
	[TestCase("#EXTM3U\n#EXT-X-MAP:URI=\"init.mp4\n", TestName = "An_unterminated_attribute_that_names_a_URI_fails")]
	public void Anything_that_is_not_a_well_formed_playlist_is_refused(string playlist)
	{
		Assert.That(Accepts(playlist), Is.False);
	}

	[TestCase(new byte[] { 0x23, 0x45, 0x58, 0x54, 0x4D, 0x33, 0x55, 0x0A }, true, TestName = "The_signature_is_recognised")]
	[TestCase(new byte[] { 0xEF, 0xBB, 0xBF, 0x23, 0x45, 0x58, 0x54, 0x4D, 0x33, 0x55 }, true, TestName = "The_signature_is_recognised_after_a_byte_order_mark")]
	[TestCase(new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 }, false, TestName = "An_mp4_header_is_not_a_playlist")]
	[TestCase(new byte[] { 0x23, 0x45, 0x58 }, false, TestName = "A_truncated_signature_is_not_a_playlist")]
	public void The_playlist_signature_is_found_in_the_first_bytes(byte[] start, bool expected)
	{
		Assert.That(HlsPlaylistRewriter.HasPlaylistSignature(start), Is.EqualTo(expected));
	}
}
