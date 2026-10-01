using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MacroDeckHost.Application.VideoStreams;
using Microsoft.AspNetCore.Http;

namespace MacroDeckHost.Tests.UnitTests.VideoStreams;

internal static class RelayScripts
{
	public static readonly TimeSpan Soon = TimeSpan.FromSeconds(10);

	public static Func<HttpContext, Task> Reply(string? contentType, string body, int status = 200)
		=> async context =>
		{
			var bytes = Encoding.UTF8.GetBytes(body);
			context.Response.StatusCode = status;
			if (contentType is not null)
			{
				context.Response.ContentType = contentType;
			}

			context.Response.ContentLength = bytes.Length;
			await context.Response.Body.WriteAsync(bytes);
		};

	public static Func<HttpContext, Task> StreamUntilAborted(TaskCompletionSource aborted)
		=> async context =>
		{
			context.Response.ContentType = "multipart/x-mixed-replace; boundary=frame";
			await context.Response.WriteAsync("--frame\r\nContent-Type: image/jpeg\r\n\r\nFRAME1\r\n");
			await context.Response.Body.FlushAsync();
			try
			{
				await Task.Delay(Timeout.Infinite, context.RequestAborted);
			}
			catch (OperationCanceledException)
			{
				aborted.TrySetResult();
			}
		};

	public static async Task<string> ReadAllAsync(HttpResponseMessage response)
		=> await response.Content.ReadAsStringAsync().WaitAsync(Soon);

	public static async Task<string> ReadFirstAsync(Stream stream)
	{
		var buffer = new byte[256];
		var read = await stream.ReadAsync(buffer).AsTask().WaitAsync(Soon);
		return Encoding.UTF8.GetString(buffer, 0, read);
	}

	// A cut stream ends as EOF or as an error depending on the transport, and both are an end.
	public static async Task ReadUntilEndedAsync(Stream stream)
	{
		var buffer = new byte[256];
		try
		{
			while (await stream.ReadAsync(buffer).AsTask().WaitAsync(Soon) > 0)
			{
			}
		}
		catch (Exception e) when (e is IOException or HttpRequestException or OperationCanceledException)
		{
		}
	}
}

[TestFixture]
[NonParallelizable]
internal sealed class VideoStreamRelayEndpointTests
{
	private const string Playlist = "#EXTM3U\n#EXT-X-TARGETDURATION:4\n#EXTINF:4.0,\nseg1.ts\n";

	private RelayTestHost _host = null!;
	private ScriptedUpstream _upstream = null!;
	private int _sessions;

	[OneTimeSetUp]
	public async Task OneTimeSetUp()
	{
		_host = await RelayTestHost.StartAsync();
		_upstream = await ScriptedUpstream.StartAsync();
	}

	[OneTimeTearDown]
	public async Task OneTimeTearDown()
	{
		await _upstream.DisposeAsync();
		await _host.DisposeAsync();
	}

	private string Arm(string path, string transport, string? sessionId = null)
		=> _host.Arm(sessionId ?? $"s{++_sessions}", _upstream.Origin + path, transport);

	[Test]
	public async Task The_first_mjpeg_chunk_arrives_before_the_upstream_finishes_and_the_boundary_survives()
	{
		var release = new TaskCompletionSource();
		var finished = false;
		_upstream.Reset(async context =>
		{
			context.Response.ContentType = "multipart/x-mixed-replace; boundary=frame";
			await context.Response.WriteAsync("--frame\r\nContent-Type: image/jpeg\r\n\r\nFRAME1\r\n");
			await context.Response.Body.FlushAsync();
			await release.Task;
			await context.Response.WriteAsync("--frame\r\nContent-Type: image/jpeg\r\n\r\nFRAME2\r\n--frame--\r\n");
			finished = true;
		});
		var relayPath = Arm("/cam.mjpg", "mjpeg");

		using var response = await _host.GetAsync(relayPath);
		await using var stream = await response.Content.ReadAsStreamAsync();
		var first = await RelayScripts.ReadFirstAsync(stream);
		var finishedWhenFirstArrived = finished;
		release.SetResult();
		var rest = await new StreamReader(stream).ReadToEndAsync().WaitAsync(RelayScripts.Soon);

		Assert.Multiple(() =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(response.Content.Headers.ContentType!.ToString(), Is.EqualTo("multipart/x-mixed-replace; boundary=frame"));
			Assert.That(first, Does.Contain("FRAME1"));
			Assert.That(finishedWhenFirstArrived, Is.False, "the client had its first frame while the upstream was still sending");
			Assert.That(rest, Does.Contain("FRAME2").And.Contain("--frame--"));
		});
	}

	[Test]
	public async Task Every_response_is_uncacheable_unsniffable_and_sandboxed()
	{
		_upstream.Reset(RelayScripts.Reply("image/jpeg", "FRAME"));
		var relayPath = Arm("/cam.jpg", "mjpeg");
		var token = RelayTestHost.TokenOf(relayPath);
		var otherwise = new[]
		{
			await _host.GetAsync(relayPath),
			await _host.GetAsync(VideoStreamRelay.PathPrefix + new string('z', 43) + "/cam.jpg"),
			await _host.GetAsync(VideoStreamRelay.PathPrefix + token + "/cam.jpg", HttpMethod.Head)
		};

		Assert.Multiple(() =>
		{
			Assert.That(otherwise.Select(r => (int)r.StatusCode), Is.EqualTo(new[] { 200, 404, 200 }));
			foreach (var response in otherwise)
			{
				Assert.That(response.Headers.CacheControl!.NoStore, Is.True);
				Assert.That(response.Headers.GetValues("X-Content-Type-Options"), Is.EqualTo(new[] { "nosniff" }));
				Assert.That(response.Headers.GetValues("Content-Security-Policy"), Is.EqualTo(new[] { "sandbox; default-src 'none'" }));
			}
		});
	}

	[Test]
	public async Task A_token_that_was_never_armed_or_is_malformed_serves_nothing()
	{
		_upstream.Reset(RelayScripts.Reply("image/jpeg", "FRAME"));
		Arm("/cam.jpg", "mjpeg");

		var unknown = await _host.GetAsync(VideoStreamRelay.PathPrefix + new string('A', 43) + "/cam.jpg");
		var tooShort = await _host.GetAsync(VideoStreamRelay.PathPrefix + "short/cam.jpg");

		Assert.Multiple(() =>
		{
			Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
			Assert.That(tooShort.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
			Assert.That(_upstream.Requests, Is.Empty);
		});
	}

	[Test]
	public async Task A_revoked_token_answers_404_and_the_stream_in_flight_is_cut()
	{
		var aborted = new TaskCompletionSource();
		_upstream.Reset(RelayScripts.StreamUntilAborted(aborted));
		var relayPath = Arm("/cam.mjpg", "mjpeg", "revoked");
		using var response = await _host.GetAsync(relayPath);
		await using var stream = await response.Content.ReadAsStreamAsync();
		await RelayScripts.ReadFirstAsync(stream);

		_host.Relay.Revoke("revoked");

		await aborted.Task.WaitAsync(RelayScripts.Soon);
		await RelayScripts.ReadUntilEndedAsync(stream);
		var after = await _host.GetAsync(relayPath);
		Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
	}

	[Test]
	public async Task Suspending_cuts_the_stream_and_resuming_serves_the_same_url_again()
	{
		var aborted = new TaskCompletionSource();
		_upstream.Reset(RelayScripts.StreamUntilAborted(aborted));
		var relayPath = Arm("/cam.mjpg", "mjpeg", "suspended");
		using var response = await _host.GetAsync(relayPath);
		await using var stream = await response.Content.ReadAsStreamAsync();
		await RelayScripts.ReadFirstAsync(stream);

		_host.Relay.Suspend("suspended");

		await aborted.Task.WaitAsync(RelayScripts.Soon);
		await RelayScripts.ReadUntilEndedAsync(stream);
		var whileSuspended = await _host.GetAsync(relayPath);
		_upstream.Reset(RelayScripts.Reply("image/jpeg", "FRAME"));
		var republished = _host.Arm("suspended", _upstream.Origin + "/cam.mjpg", "mjpeg");
		var afterResume = await _host.GetAsync(relayPath);

		Assert.Multiple(() =>
		{
			Assert.That(whileSuspended.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
			Assert.That(republished, Is.EqualTo(relayPath));
			Assert.That(afterResume.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		});
	}

	[Test]
	public async Task A_head_request_goes_upstream_as_head_and_answers_with_headers_only()
	{
		_upstream.Reset(RelayScripts.Reply("image/jpeg", "FRAME"));
		var relayPath = Arm("/cam.jpg", "mjpeg");

		using var response = await _host.GetAsync(relayPath, HttpMethod.Head);

		Assert.Multiple(() =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("image/jpeg"));
			Assert.That(response.Content.Headers.ContentLength, Is.EqualTo(5));
			Assert.That(_upstream.Requests.Select(r => r.Method), Is.EqualTo(new[] { "HEAD" }));
		});
		Assert.That(await RelayScripts.ReadAllAsync(response), Is.Empty);
	}

	[Test]
	public async Task Only_get_and_head_are_relayed()
	{
		_upstream.Reset(RelayScripts.Reply("image/jpeg", "FRAME"));
		var relayPath = Arm("/cam.jpg", "mjpeg");

		var post = await _host.GetAsync(relayPath, HttpMethod.Post);
		var delete = await _host.GetAsync(relayPath, HttpMethod.Delete);

		Assert.Multiple(() =>
		{
			Assert.That((int)post.StatusCode, Is.GreaterThanOrEqualTo(400));
			Assert.That((int)delete.StatusCode, Is.GreaterThanOrEqualTo(400));
			Assert.That(_upstream.Requests, Is.Empty);
		});
	}

	[Test]
	public async Task A_range_request_passes_through_as_a_partial_response_for_media()
	{
		_upstream.Reset(async context =>
		{
			context.Response.StatusCode = StatusCodes.Status206PartialContent;
			context.Response.ContentType = "video/mp2t";
			context.Response.Headers.ContentRange = "bytes 0-3/100";
			context.Response.Headers.AcceptRanges = "bytes";
			await context.Response.WriteAsync("ABCD");
		});
		var relayPath = Arm("/live/seg1.ts", "hls");

		using var response = await _host.GetAsync(relayPath, configure: r => r.Headers.Range = new RangeHeaderValue(0, 3));

		Assert.Multiple(() =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PartialContent));
			Assert.That(response.Content.Headers.ContentRange!.ToString(), Is.EqualTo("bytes 0-3/100"));
			Assert.That(response.Headers.AcceptRanges, Is.EqualTo(new[] { "bytes" }));
			Assert.That(_upstream.Requests.Single().Range, Is.EqualTo("bytes=0-3"));
		});
		Assert.That(await RelayScripts.ReadAllAsync(response), Is.EqualTo("ABCD"));
	}

	[Test]
	public async Task A_range_is_never_sent_for_a_playlist_and_a_partial_answer_to_one_is_refused()
	{
		_upstream.Reset(async context =>
		{
			context.Response.StatusCode = StatusCodes.Status206PartialContent;
			context.Response.ContentType = "application/vnd.apple.mpegurl";
			context.Response.Headers.ContentRange = "bytes 0-5/100";
			await context.Response.WriteAsync(Playlist);
		});
		var relayPath = Arm("/live/index.m3u8", "hls");

		using var response = await _host.GetAsync(relayPath, configure: r => r.Headers.Range = new RangeHeaderValue(0, 5));

		Assert.Multiple(() =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
			Assert.That(_upstream.Requests.Single().Range, Is.Null);
		});
	}

	private static readonly string[] _mediaTypeRows =
	[
		"mjpeg|/cam|multipart/x-mixed-replace; boundary=f|200",
		"mjpeg|/cam|image/jpeg|200",
		"mjpeg|/cam|IMAGE/JPEG|200",
		"mjpeg|/cam|application/octet-stream|502",
		"mjpeg|/cam|video/mp2t|502",
		"mjpeg|/cam|text/html|502",
		"mjpeg|/cam||502",
		"hls|/live/s.ts|video/mp2t|200",
		"hls|/live/s.ts|VIDEO/MP2T|200",
		"hls|/live/s.mp4|video/mp4|200",
		"hls|/live/s.m4s|video/iso.segment|200",
		"hls|/live/s.aac|audio/aac|200",
		"hls|/live/s.vtt|text/vtt|200",
		"hls|/live/s.bin|application/octet-stream|200",
		"hls|/live/s.bin||200",
		"hls|/live/s.ts|text/html|502",
		"hls|/live/s.ts|image/jpeg|502",
		"hls|/live/s.ts|application/javascript|502",
		"hls|/live/s.txt|text/plain|502",
		"hls|/live/x.m3u8|text/plain|200",
		"hls|/live/x.m3u8|application/octet-stream|200",
		"hls|/live/x.m3u8|text/html|502",
		"hls|/live/x|Application/X-MPEGURL|200",
		"hls|/live/x|application/vnd.apple.mpegurl|200"
	];

	[TestCaseSource(nameof(_mediaTypeRows))]
	public async Task Only_content_types_that_fit_the_sessions_transport_are_passed_on(string row)
	{
		var (transport, path, contentType, expected) = Split(row);
		var isPlaylist = path.EndsWith(".m3u8", StringComparison.Ordinal) || path == "/live/x";
		_upstream.Reset(RelayScripts.Reply(contentType.Length == 0 ? null : contentType,
			isPlaylist ? Playlist : "SECRET-BYTES"));
		var relayPath = Arm(path, transport);

		using var response = await _host.GetAsync(relayPath);
		var body = await RelayScripts.ReadAllAsync(response);

		Assert.Multiple(() =>
		{
			Assert.That((int)response.StatusCode, Is.EqualTo(expected));
			if (expected == 502)
			{
				Assert.That(body, Does.Not.Contain("SECRET-BYTES").And.Not.Contain("seg1.ts"), "the upstream body is never forwarded");
			}
		});

		static (string, string, string, int) Split(string row)
		{
			var parts = row.Split('|');
			return (parts[0], parts[1], parts[2], int.Parse(parts[3], CultureInfo.InvariantCulture));
		}
	}

	[Test]
	public async Task An_html_page_is_never_forwarded_even_when_it_comes_with_an_error_status()
	{
		_upstream.Reset(RelayScripts.Reply("text/html", "<script>SECRET</script>", StatusCodes.Status404NotFound));
		var relayPath = Arm("/live/s.ts", "hls");

		using var response = await _host.GetAsync(relayPath);

		Assert.Multiple(() =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
		});
		Assert.That(await RelayScripts.ReadAllAsync(response), Does.Not.Contain("SECRET"));
	}

	[Test]
	public async Task An_upstream_error_status_without_a_body_type_passes_through_empty()
	{
		_upstream.Reset(RelayScripts.Reply(null, "", StatusCodes.Status404NotFound));
		var relayPath = Arm("/live/missing.ts", "hls");

		using var response = await _host.GetAsync(relayPath);

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
	}

	[Test]
	public async Task A_response_that_arrives_compressed_is_refused_rather_than_passed_on_undecodable()
	{
		_upstream.Reset(async context =>
		{
			context.Response.ContentType = "video/mp2t";
			context.Response.Headers.ContentEncoding = "gzip";
			await context.Response.WriteAsync("not really gzip");
		});
		var relayPath = Arm("/live/s.ts", "hls");

		using var response = await _host.GetAsync(relayPath);

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
	}

	[Test]
	public async Task A_transport_change_keeps_the_url_and_switches_what_the_relay_accepts()
	{
		_upstream.Reset(RelayScripts.Reply("video/mp2t", "SEGMENT"));
		var asHls = Arm("/live/s.ts", "hls", "switching");
		var hlsResponse = await _host.GetAsync(asHls);

		var asMjpeg = _host.Arm("switching", _upstream.Origin + "/live/s.ts", "mjpeg");
		var mjpegResponse = await _host.GetAsync(asMjpeg);

		Assert.Multiple(() =>
		{
			Assert.That(asMjpeg, Is.EqualTo(asHls));
			Assert.That(hlsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(mjpegResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
		});
	}

	[Test]
	public async Task A_playlist_reaches_the_client_with_every_uri_sent_through_the_relay()
	{
		_upstream.Reset(async context =>
		{
			context.Response.ContentType = "application/vnd.apple.mpegurl";
			await context.Response.WriteAsync(
				$"#EXTM3U\n#EXT-X-MAP:URI=\"init.mp4\"\nseg1.ts\n{_upstream.Origin}/live/seg2.ts?k=v\n/top/seg3.ts\n");
		});
		var relayPath = Arm("/live/index.m3u8", "hls");
		var prefix = VideoStreamRelay.PathPrefix + RelayTestHost.TokenOf(relayPath);

		using var response = await _host.GetAsync(relayPath);
		var body = await RelayScripts.ReadAllAsync(response);

		Assert.Multiple(() =>
		{
			Assert.That(response.Content.Headers.ContentType!.ToString(), Is.EqualTo("application/vnd.apple.mpegurl"));
			Assert.That(response.Content.Headers.ContentLength, Is.EqualTo(Encoding.UTF8.GetByteCount(body)));
			Assert.That(body, Is.EqualTo($"#EXTM3U\n#EXT-X-MAP:URI=\"{prefix}/live/init.mp4\"\n{prefix}/live/seg1.ts\n{prefix}/live/seg2.ts?k=v\n{prefix}/top/seg3.ts\n"));
			Assert.That(body, Does.Not.Contain(_upstream.Origin));
		});
	}

	[Test]
	public async Task A_playlist_that_names_a_segment_on_another_origin_is_refused()
	{
		_upstream.Reset(RelayScripts.Reply("application/vnd.apple.mpegurl",
			"#EXTM3U\nseg1.ts\nhttp://localhost:" + _upstream.Port + "/live/seg2.ts\n"));
		var relayPath = Arm("/live/index.m3u8", "hls");

		using var response = await _host.GetAsync(relayPath);

		Assert.Multiple(() =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
		});
		Assert.That(await RelayScripts.ReadAllAsync(response), Does.Not.Contain("seg1.ts"));
	}

	[Test]
	public async Task A_playlist_served_as_plain_bytes_from_a_path_without_an_extension_is_found_by_its_first_line()
	{
		_upstream.Reset(RelayScripts.Reply("application/octet-stream", "#EXTM3U\nseg1.ts\n"));
		var relayPath = Arm("/live/playlist", "hls");
		var prefix = VideoStreamRelay.PathPrefix + RelayTestHost.TokenOf(relayPath);

		using var response = await _host.GetAsync(relayPath);

		Assert.Multiple(() =>
		{
			Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/vnd.apple.mpegurl"));
			Assert.That(RelayScripts.ReadAllAsync(response).Result, Is.EqualTo($"#EXTM3U\n{prefix}/live/seg1.ts\n"));
		});
	}

	[Test]
	public async Task A_segment_served_as_plain_bytes_is_passed_on_untouched()
	{
		_upstream.Reset(RelayScripts.Reply("application/octet-stream", "GIF89a-not-a-playlist-seg1.ts"));
		var relayPath = Arm("/live/s.bin", "hls");

		using var response = await _host.GetAsync(relayPath);

		Assert.That(await RelayScripts.ReadAllAsync(response), Is.EqualTo("GIF89a-not-a-playlist-seg1.ts"));
	}

	[TestCase("application/vnd.apple.mpegurl", "<html>not a playlist</html>", TestName = "A_playlist_type_without_the_extm3u_line_is_refused")]
	[TestCase("application/vnd.apple.mpegurl", "#EXTM3U\n#EXT-X-MAP:URI=\"x.mp4\nseg.ts\n", TestName = "A_playlist_with_a_broken_uri_attribute_is_refused")]
	public async Task A_body_that_is_not_a_playlist_is_refused_when_the_type_or_path_says_it_is(string contentType, string body)
	{
		_upstream.Reset(RelayScripts.Reply(contentType, body));
		var relayPath = Arm("/live/index.m3u8", "hls");

		using var response = await _host.GetAsync(relayPath);

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
	}

	[Test]
	public async Task A_playlist_over_one_mebibyte_is_refused()
	{
		var huge = "#EXTM3U\n" + string.Concat(Enumerable.Repeat("seg.ts\n", 160_000));
		_upstream.Reset(RelayScripts.Reply("application/vnd.apple.mpegurl", huge));
		var relayPath = Arm("/live/index.m3u8", "hls");

		using var response = await _host.GetAsync(relayPath);

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
	}

	[Test]
	public async Task A_relative_playlist_uri_resolves_against_the_url_after_a_redirect()
	{
		_upstream.Reset(async context =>
		{
			if (context.Request.Path == "/live/index.m3u8")
			{
				context.Response.StatusCode = StatusCodes.Status302Found;
				context.Response.Headers.Location = "../moved/dir/index.m3u8";
				return;
			}

			context.Response.ContentType = "application/vnd.apple.mpegurl";
			await context.Response.WriteAsync("#EXTM3U\nseg.ts\n");
		});
		var relayPath = Arm("/live/index.m3u8", "hls");
		var prefix = VideoStreamRelay.PathPrefix + RelayTestHost.TokenOf(relayPath);

		using var response = await _host.GetAsync(relayPath);

		Assert.That(await RelayScripts.ReadAllAsync(response), Is.EqualTo($"#EXTM3U\n{prefix}/moved/dir/seg.ts\n"));
	}

	[Test]
	public async Task A_redirect_inside_the_origin_is_followed_absolute_or_relative()
	{
		_upstream.Reset(async context =>
		{
			switch (context.Request.Path.Value)
			{
				case "/a/start":
					context.Response.StatusCode = StatusCodes.Status307TemporaryRedirect;
					context.Response.Headers.Location = _upstream.Origin + "/a/middle";
					break;
				case "/a/middle":
					context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
					context.Response.Headers.Location = "../b/final.jpg";
					break;
				default:
					context.Response.ContentType = "image/jpeg";
					await context.Response.WriteAsync("FINAL");
					break;
			}
		});
		var relayPath = Arm("/a/start", "mjpeg");

		using var response = await _host.GetAsync(relayPath);

		Assert.Multiple(() =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(_upstream.Requests.Select(r => r.RawTarget), Is.EqualTo(new[] { "/a/start", "/a/middle", "/b/final.jpg" }));
		});
		Assert.That(await RelayScripts.ReadAllAsync(response), Is.EqualTo("FINAL"));
	}

	[TestCase("http://127.0.0.1:{other}/x", TestName = "A_redirect_to_another_port_is_refused")]
	[TestCase("http://localhost:{own}/x", TestName = "A_redirect_to_the_same_host_under_another_name_is_refused")]
	[TestCase("https://127.0.0.1:{own}/x", TestName = "A_redirect_to_another_scheme_is_refused")]
	[TestCase("//elsewhere.example/x", TestName = "A_redirect_to_a_network_path_reference_is_refused")]
	public async Task A_redirect_that_leaves_the_pinned_origin_is_not_followed(string location)
	{
		await using var other = await ScriptedUpstream.StartAsync();
		other.Reset(RelayScripts.Reply("image/jpeg", "OTHER"));
		_upstream.Reset(context =>
		{
			context.Response.StatusCode = StatusCodes.Status302Found;
			context.Response.Headers.Location = location
				.Replace("{other}", other.Port.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
				.Replace("{own}", _upstream.Port.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
			return Task.CompletedTask;
		});
		var relayPath = Arm("/cam", "mjpeg");

		using var response = await _host.GetAsync(relayPath);

		Assert.Multiple(() =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
			Assert.That(other.Requests, Is.Empty);
			Assert.That(_upstream.Requests, Has.Count.EqualTo(1));
		});
	}

	[TestCase(3, 200, TestName = "Three_redirects_are_followed")]
	[TestCase(4, 502, TestName = "A_fourth_redirect_is_refused")]
	public async Task Redirects_are_followed_only_a_few_times(int redirects, int expected)
	{
		_upstream.Reset(async context =>
		{
			var hop = int.Parse(context.Request.Path.Value!.TrimStart('/').Replace("hop", "", StringComparison.Ordinal),
				CultureInfo.InvariantCulture);
			if (hop < redirects)
			{
				context.Response.StatusCode = StatusCodes.Status302Found;
				context.Response.Headers.Location = $"/hop{hop + 1}";
				return;
			}

			context.Response.ContentType = "image/jpeg";
			await context.Response.WriteAsync("END");
		});
		var relayPath = Arm("/hop0", "mjpeg");

		using var response = await _host.GetAsync(relayPath);

		Assert.That((int)response.StatusCode, Is.EqualTo(expected));
	}

	[Test]
	public async Task At_most_eight_requests_run_at_once_per_session_and_a_finished_one_frees_its_slot()
	{
		var release = new TaskCompletionSource();
		_upstream.Reset(async context =>
		{
			context.Response.ContentType = "multipart/x-mixed-replace; boundary=frame";
			await context.Response.WriteAsync("--frame\r\n\r\nF\r\n");
			await context.Response.Body.FlushAsync();
			await release.Task;
		});
		var relayPath = Arm("/cam.mjpg", "mjpeg");

		var holding = new List<HttpResponseMessage>();
		for (var i = 0; i < VideoStreamRelay.MaxLeasesPerSession; i++)
		{
			holding.Add(await _host.GetAsync(relayPath));
		}

		var ninth = await _host.GetAsync(relayPath);
		release.SetResult();
		foreach (var response in holding)
		{
			await RelayScripts.ReadAllAsync(response);
		}

		HttpStatusCode? afterwards = null;
		for (var attempt = 0; attempt < 50 && afterwards != HttpStatusCode.OK; attempt++)
		{
			using var retry = await _host.GetAsync(relayPath);
			afterwards = retry.StatusCode;
			if (afterwards != HttpStatusCode.OK)
			{
				await Task.Delay(100);
			}
		}

		Assert.Multiple(() =>
		{
			Assert.That(holding.Select(r => r.StatusCode), Is.All.EqualTo(HttpStatusCode.OK));
			Assert.That(ninth.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
			Assert.That(afterwards, Is.EqualTo(HttpStatusCode.OK));
		});
		holding.ForEach(r => r.Dispose());
		ninth.Dispose();
	}
}
