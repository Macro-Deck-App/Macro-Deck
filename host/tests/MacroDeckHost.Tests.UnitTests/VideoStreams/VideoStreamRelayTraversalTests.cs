using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MacroDeckHost.Application.VideoStreams;
using Microsoft.AspNetCore.Http;

namespace MacroDeckHost.Tests.UnitTests.VideoStreams;

[TestFixture]
[NonParallelizable]
internal sealed class VideoStreamRelayTraversalTests
{
	private RelayTestHost _host = null!;
	private RelayTestHost _kestrel = null!;
	private ScriptedUpstream _upstream = null!;
	private string _prefix = null!;
	private string _kestrelPrefix = null!;

	[OneTimeSetUp]
	public async Task OneTimeSetUp()
	{
		_upstream = await ScriptedUpstream.StartAsync();
		_upstream.Handler = RelayScripts.Reply("image/jpeg", "FRAME");
		_host = await RelayTestHost.StartAsync();
		_prefix = _host.Arm("s", _upstream.Origin + "/live/cam.jpg", "mjpeg")[..(VideoStreamRelay.PathPrefix.Length + 43)];
		_kestrel = await RelayTestHost.StartAsync(kestrel: true);
		_kestrelPrefix = _kestrel.Arm("s", _upstream.Origin + "/live/cam.jpg", "mjpeg")[..(VideoStreamRelay.PathPrefix.Length + 43)];
	}

	[OneTimeTearDown]
	public async Task OneTimeTearDown()
	{
		await _kestrel.DisposeAsync();
		await _host.DisposeAsync();
		await _upstream.DisposeAsync();
	}

	[SetUp]
	public void SetUp() => _upstream.Requests.Clear();

	[TestCase("/live/%2e%2e/secret", TestName = "A_dot_segment_spelled_with_percent_escapes_is_refused")]
	[TestCase("/live/%2E%2E/secret", TestName = "A_dot_segment_with_uppercase_escapes_is_refused")]
	[TestCase("/live/../secret", TestName = "A_literal_dot_segment_is_refused")]
	[TestCase("/live/./secret", TestName = "A_single_dot_segment_is_refused")]
	[TestCase("/..", TestName = "A_trailing_dot_segment_is_refused")]
	[TestCase("/live%2Fsecret", TestName = "An_encoded_slash_is_refused")]
	[TestCase("/live/a%2fb", TestName = "An_encoded_slash_in_lower_case_is_refused")]
	[TestCase("/live/a%5Cb", TestName = "An_encoded_backslash_is_refused")]
	[TestCase("/live/a\\b", TestName = "A_literal_backslash_is_refused")]
	[TestCase("/live/a%00b", TestName = "A_NUL_character_is_refused")]
	[TestCase("/live/a%0D%0AHost:evil", TestName = "A_line_break_in_the_path_is_refused")]
	[TestCase("/live/a\r\nHost:evil", TestName = "A_literal_line_break_in_the_path_is_refused")]
	[TestCase("/live/a%09b", TestName = "A_control_character_in_the_path_is_refused")]
	public async Task A_path_that_could_leave_the_origin_or_its_directory_is_never_sent_upstream(string tail)
	{
		var context = await _host.SendRawPathAsync(_prefix + tail);

		Assert.Multiple(() =>
		{
			Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
			Assert.That(_upstream.Requests, Is.Empty);
		});
	}

	[TestCase("//evil.example/x", "//evil.example/x", TestName = "A_path_that_starts_with_two_slashes_stays_a_path_on_the_pinned_origin")]
	[TestCase("/live/a%3Fb", "/live/a%3Fb", TestName = "An_encoded_question_mark_stays_part_of_the_path")]
	[TestCase("/live/a%23b", "/live/a%23b", TestName = "An_encoded_hash_stays_part_of_the_path")]
	[TestCase("/live/a b", "/live/a%20b", TestName = "A_space_is_sent_encoded")]
	[TestCase("/live/%252e%252e/x", "/live/%252e%252e/x", TestName = "A_double_encoded_dot_segment_reaches_the_upstream_as_the_literal_name_it_is")]
	public async Task A_path_the_relay_accepts_arrives_upstream_on_the_pinned_origin_as_sent(string tail, string expectedTarget)
	{
		var context = await _host.SendRawPathAsync(_prefix + tail);

		Assert.Multiple(() =>
		{
			Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
			Assert.That(_upstream.Requests.Select(r => r.RawTarget), Is.EqualTo(new[] { expectedTarget }));
		});
	}

	[Test]
	public async Task A_query_with_an_encoded_slash_is_legitimate_and_reaches_the_upstream_verbatim()
	{
		var context = await _host.SendRawPathAsync(_prefix + "/live/x", "?url=a%2Fb&n=1");

		Assert.Multiple(() =>
		{
			Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
			Assert.That(_upstream.Requests.Single().RawTarget, Is.EqualTo("/live/x?url=a%2Fb&n=1"));
		});
	}

	[TestCase("?a=b\r\nHost: evil", TestName = "A_raw_line_break_in_the_query_is_refused")]
	[TestCase("?a=b\nx", TestName = "A_raw_line_feed_in_the_query_is_refused")]
	[TestCase("?a=b\0", TestName = "A_raw_NUL_in_the_query_is_refused")]
	public async Task A_query_that_could_split_the_upstream_request_is_never_sent(string query)
	{
		var context = await _host.SendRawPathAsync(_prefix + "/live/x", query);

		Assert.Multiple(() =>
		{
			Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
			Assert.That(_upstream.Requests, Is.Empty);
		});
	}

	[TestCase("/live/%252e%252e/secret", TestName = "Kestrel_hands_a_double_encoded_dot_segment_on_as_a_single_encoded_one_and_it_is_refused")]
	[TestCase("/live/a%2Fb", TestName = "Kestrel_keeps_an_encoded_slash_and_it_is_refused")]
	[TestCase("/live/a%252Fb", TestName = "Kestrel_turns_a_double_encoded_slash_into_an_encoded_one_and_it_is_refused")]
	[TestCase("/live/a%5Cb", TestName = "Kestrel_decodes_an_encoded_backslash_and_it_is_refused")]
	public async Task Un_normalised_request_lines_the_relay_receives_are_refused_through_a_real_listener(string tail)
	{
		var status = await SendRawRequestAsync(_kestrel.Listener!, _kestrelPrefix + tail);

		Assert.Multiple(() =>
		{
			Assert.That(status, Is.EqualTo(404));
			Assert.That(_upstream.Requests, Is.Empty);
		});
	}

	[TestCase("/live/%2e%2e/%2e%2e/secret", TestName = "Kestrel_normalises_encoded_dot_segments_away_from_the_relay")]
	[TestCase("/live/../../../secret", TestName = "Kestrel_normalises_literal_dot_segments_away_from_the_relay")]
	[TestCase("/live/a\\b", TestName = "Kestrel_lets_a_raw_backslash_no_further_than_a_refusal")]
	public async Task Dot_segments_and_raw_backslashes_on_a_real_listener_never_reach_the_upstream(string tail)
	{
		await SendRawRequestAsync(_kestrel.Listener!, _kestrelPrefix + tail);

		Assert.That(_upstream.Requests, Is.Empty);
	}

	[Test]
	public async Task A_request_line_with_a_double_slash_reaches_the_pinned_origin_and_nothing_else_through_a_real_listener()
	{
		var status = await SendRawRequestAsync(_kestrel.Listener!, _kestrelPrefix + "//evil.example/x");

		Assert.Multiple(() =>
		{
			Assert.That(status, Is.EqualTo(200));
			Assert.That(_upstream.Requests.Select(r => r.RawTarget), Is.EqualTo(new[] { "//evil.example/x" }));
		});
	}

	private static async Task<int> SendRawRequestAsync(IPEndPoint endpoint, string target)
	{
		using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
		await socket.ConnectAsync(endpoint);
		await socket.SendAsync(Encoding.ASCII.GetBytes($"GET {target} HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n"));
		var received = new StringBuilder();
		var buffer = new byte[4096];
		using var timeout = new CancellationTokenSource(RelayScripts.Soon);
		int read;
		while ((read = await socket.ReceiveAsync(buffer, SocketFlags.None, timeout.Token)) > 0)
		{
			received.Append(Encoding.ASCII.GetString(buffer, 0, read));
		}

		var statusLine = received.ToString().Split("\r\n")[0].Split(' ');
		return int.Parse(statusLine[1], CultureInfo.InvariantCulture);
	}
}
