using MacroDeckHost.Integrations.Streamerbot;

namespace MacroDeckHost.Tests.UnitTests.Streamerbot;

[TestFixture]
internal sealed class StreamerbotEndpointTests
{
	[Test]
	public void Build_uses_the_endpoint_path()
	{
		var uri = StreamerbotEndpoint.Build("127.0.0.1", 8080, "/ws");

		Assert.That(uri.ToString(), Is.EqualTo("ws://127.0.0.1:8080/ws"));
	}

	[Test]
	public void Build_defaults_an_empty_endpoint_to_root()
	{
		var uri = StreamerbotEndpoint.Build("127.0.0.1", 8080, "   ");

		Assert.That(uri.ToString(), Is.EqualTo("ws://127.0.0.1:8080/"));
	}

	[Test]
	public void NormaliseEndpoint_adds_the_leading_slash()
	{
		Assert.That(StreamerbotEndpoint.NormaliseEndpoint("ws"), Is.EqualTo("/ws"));
	}

	[TestCase("ws://192.168.0.5:8080/", "192.168.0.5")]
	[TestCase("http://streamer.local", "streamer.local")]
	[TestCase("192.168.0.5:8080", "192.168.0.5")]
	[TestCase(" 127.0.0.1 ", "127.0.0.1")]
	public void NormaliseHost_strips_what_users_paste_from_the_settings_page(string input, string expected)
	{
		Assert.That(StreamerbotEndpoint.NormaliseHost(input), Is.EqualTo(expected));
	}

	[Test]
	public void NormaliseHost_keeps_a_bare_hostname()
	{
		Assert.That(StreamerbotEndpoint.NormaliseHost("streaming-pc"), Is.EqualTo("streaming-pc"));
	}

	[Test]
	public void NormaliseHost_falls_back_to_the_default_when_nothing_is_left()
	{
		Assert.That(StreamerbotEndpoint.NormaliseHost("ws://"), Is.EqualTo(StreamerbotEndpoint.DefaultHost));
	}
}
