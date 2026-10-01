using System.Net;
using System.Text;
using MacroDeckHost.Application.VideoStreams;
using Microsoft.AspNetCore.Http;

namespace MacroDeckHost.Tests.UnitTests.VideoStreams;

[TestFixture]
[NonParallelizable]
internal sealed class VideoStreamRelayLimitTests
{
	private static readonly TimeSpan _headerTimeout = TimeSpan.FromMilliseconds(400);
	private static readonly TimeSpan _idleTimeout = TimeSpan.FromMilliseconds(600);

	private RelayTestHost _host = null!;
	private ScriptedUpstream _upstream = null!;

	[OneTimeSetUp]
	public async Task OneTimeSetUp()
	{
		_upstream = await ScriptedUpstream.StartAsync();
		_host = await RelayTestHost.StartAsync(new VideoStreamRelayOptions
		{
			ResponseHeaderTimeout = _headerTimeout,
			IdleReadTimeout = _idleTimeout
		});
	}

	[OneTimeTearDown]
	public async Task OneTimeTearDown()
	{
		await _host.DisposeAsync();
		await _upstream.DisposeAsync();
	}

	[Test]
	public async Task An_upstream_that_does_not_start_answering_in_time_is_a_gateway_timeout_and_is_dropped()
	{
		var aborted = new TaskCompletionSource();
		_upstream.Reset(async context =>
		{
			try
			{
				await Task.Delay(Timeout.Infinite, context.RequestAborted);
			}
			catch (OperationCanceledException)
			{
				aborted.TrySetResult();
			}
		});
		var relayPath = _host.Arm("header", _upstream.Origin + "/cam.mjpg", "mjpeg");

		using var response = await _host.GetAsync(relayPath);

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.GatewayTimeout));
		await aborted.Task.WaitAsync(RelayScripts.Soon);
	}

	[Test]
	public async Task A_stream_that_stays_silent_past_the_idle_timeout_is_cut()
	{
		var aborted = new TaskCompletionSource();
		_upstream.Reset(RelayScripts.StreamUntilAborted(aborted));
		var relayPath = _host.Arm("idle", _upstream.Origin + "/cam.mjpg", "mjpeg");
		using var response = await _host.GetAsync(relayPath);
		await using var stream = await response.Content.ReadAsStreamAsync();
		var first = await RelayScripts.ReadFirstAsync(stream);

		await RelayScripts.ReadUntilEndedAsync(stream);

		Assert.That(first, Does.Contain("FRAME1"));
		await aborted.Task.WaitAsync(RelayScripts.Soon);
	}

	[Test]
	public async Task A_stream_that_keeps_sending_outlives_both_timeouts()
	{
		_upstream.Reset(async context =>
		{
			context.Response.ContentType = "multipart/x-mixed-replace; boundary=frame";
			for (var frame = 1; frame <= 8; frame++)
			{
				await context.Response.WriteAsync($"--frame\r\n\r\nFRAME{frame}\r\n");
				await context.Response.Body.FlushAsync();
				await Task.Delay(200);
			}
		});
		var relayPath = _host.Arm("alive", _upstream.Origin + "/cam.mjpg", "mjpeg");

		using var response = await _host.GetAsync(relayPath);
		var body = await RelayScripts.ReadAllAsync(response);

		Assert.That(body, Does.Contain("FRAME1").And.Contain("FRAME8"));
	}

	[Test]
	public async Task A_playlist_that_blocks_before_its_body_is_not_cut_by_the_idle_timeout()
	{
		_upstream.Reset(async context =>
		{
			context.Response.ContentType = "application/vnd.apple.mpegurl";
			await context.Response.StartAsync();
			await context.Response.Body.FlushAsync();
			await Task.Delay(_idleTimeout * 2);
			await context.Response.WriteAsync("#EXTM3U\nseg.ts\n");
		});
		var relayPath = _host.Arm("blocking", _upstream.Origin + "/live/index.m3u8", "hls");

		using var response = await _host.GetAsync(relayPath);

		Assert.Multiple(() =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		});
		Assert.That(await RelayScripts.ReadAllAsync(response), Does.Contain("/live/seg.ts"));
	}
}

[TestFixture]
[NonParallelizable]
internal sealed class VideoStreamRelayListenerTests
{
	[Test]
	public async Task A_client_that_goes_away_aborts_the_request_to_the_upstream()
	{
		await using var upstream = await ScriptedUpstream.StartAsync();
		var aborted = new TaskCompletionSource();
		upstream.Handler = RelayScripts.StreamUntilAborted(aborted);
		await using var host = await RelayTestHost.StartAsync(kestrel: true);
		var relayPath = host.Arm("gone", upstream.Origin + "/cam.mjpg", "mjpeg");
		var response = await host.GetAsync(relayPath);
		await using var stream = await response.Content.ReadAsStreamAsync();
		await RelayScripts.ReadFirstAsync(stream);

		response.Dispose();

		await aborted.Task.WaitAsync(RelayScripts.Soon);
	}

	[Test]
	public async Task Stopping_the_host_with_a_stream_in_flight_does_not_hang_and_drops_the_upstream()
	{
		await using var upstream = await ScriptedUpstream.StartAsync();
		var aborted = new TaskCompletionSource();
		upstream.Handler = RelayScripts.StreamUntilAborted(aborted);
		var host = await RelayTestHost.StartAsync(kestrel: true);
		try
		{
			var relayPath = host.Arm("shutdown", upstream.Origin + "/cam.mjpg", "mjpeg");
			using var response = await host.GetAsync(relayPath);
			await using var stream = await response.Content.ReadAsStreamAsync();
			await RelayScripts.ReadFirstAsync(stream);

			await host.StopAsync().WaitAsync(RelayScripts.Soon);

			await aborted.Task.WaitAsync(RelayScripts.Soon);
			await RelayScripts.ReadUntilEndedAsync(stream);
		}
		finally
		{
			await host.DisposeAsync();
		}
	}
}

[TestFixture]
[NonParallelizable]
internal sealed class VideoStreamRelayLogTests
{
	[Test]
	public async Task The_token_never_reaches_the_log_even_with_every_framework_category_at_debug()
	{
		await using var upstream = await ScriptedUpstream.StartAsync();
		upstream.Handler = RelayScripts.Reply("image/jpeg", "FRAME");
		var host = await RelayTestHost.StartAsync(realLogging: true);
		try
		{
			var relayPath = host.Arm("logged", upstream.Origin + "/cam.jpg?secret=abc", "mjpeg");
			var token = RelayTestHost.TokenOf(relayPath);
			using var served = await host.GetAsync(relayPath);
			using var notFound = await host.GetAsync(VideoStreamRelay.PathPrefix + token + "x/cam.jpg");
			using var doubledRelaySlash = await host.GetAsync($"/api/video-streams/relay//{token}/live/seg1.ts");
			using var doubledSegmentSlash = await host.GetAsync($"/api/video-streams//relay/{token}/live/seg1.ts");
			await host.StopAsync();

			var logText = await ReadLogsAsync(host.LogsDirectory);
			Assert.Multiple(() =>
			{
				Assert.That(served.StatusCode, Is.EqualTo(HttpStatusCode.OK));
				Assert.That(logText, Does.Contain("/api/video-streams/relay/"), "the requests were logged at all");
				Assert.That(logText, Does.Not.Contain(token));
				Assert.That(logText, Does.Not.Contain("secret=abc"));
			});
		}
		finally
		{
			await host.DisposeAsync();
		}
	}

	private static async Task<string> ReadLogsAsync(string directory)
	{
		var text = new StringBuilder();
		foreach (var file in Directory.EnumerateFiles(directory, "host-*.log"))
		{
			await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using var reader = new StreamReader(stream);
			text.Append(await reader.ReadToEndAsync());
		}

		return text.ToString();
	}
}
