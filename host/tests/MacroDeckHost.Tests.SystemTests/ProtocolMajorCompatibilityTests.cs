using MacroDeckHost.Tests.SystemTests.Support;

namespace MacroDeckHost.Tests.SystemTests;

// COMPAT-01, protocol half. The compatibility policy keeps majors 1 and 2 fully served and negotiates once:
// negotiated = min(clientMax, current), refused below max(clientMin, minimum).
[TestFixture]
internal sealed class ProtocolMajorCompatibilityTests : SystemTestFixture
{
	private const string PluginId = "com.example.compat-probe";

	[TestCase(1)]
	[TestCase(2)]
	public async Task A_plugin_capped_at_an_older_major_negotiates_it_and_runs_a_session(int major)
	{
		var host = await StartHostAsync();
		var credential = await RawProtocolPlugin.EnrollAsync(host, PluginId);

		var (status, session) = await RawProtocolPlugin.RequestSessionAsync(host, credential, 1, major);
		Assert.That(status, Is.EqualTo(201), session.ToJsonString());

		await using var plugin = await RawProtocolPlugin.ConnectAsync(host, session);
		var welcome = await plugin.HelloAsync();
		var pong = await plugin.PingAsync();
		var sessions = await host.GetJsonAsync("/api/plugin-sessions");
		var listed = sessions["sessions"]!.AsArray().Single(entry => (string?)entry!["pluginId"] == PluginId)!;
		var events = await Poll.ForAsync(async () =>
			{
				var json = (await host.GetJsonAsync("/api/events")).ToJsonString();
				return json.Contains("Compat probe fired", StringComparison.Ordinal) ? json : null;
			},
			TimeSpan.FromSeconds(30),
			"the event catalog the plugin described reached the host");
		await plugin.GoodbyeAsync();

		Assert.Multiple(() =>
		{
			Assert.That((int?)session["negotiatedVersion"], Is.EqualTo(major));
			var eventsCapability = session["capabilities"]!.AsArray()
				.Single(result => (string?)result!["kind"] == "events")!;
			Assert.That((bool?)eventsCapability["accepted"], Is.True, session.ToJsonString());
			Assert.That((int?)eventsCapability["negotiatedVersion"], Is.EqualTo(1));
			Assert.That((string?)welcome["type"], Is.EqualTo("session.welcome"));
			Assert.That((string?)welcome["payload"]!["sessionId"], Is.EqualTo(plugin.SessionId));
			Assert.That((bool?)welcome["payload"]!["resumed"], Is.False);
			Assert.That((string?)pong["type"], Is.EqualTo("session.pong"));
			Assert.That((int?)listed["negotiatedVersion"], Is.EqualTo(major));
			Assert.That(events, Does.Contain("Compat probe fired"));
		});
	}

	[Test]
	public async Task A_plugin_that_needs_a_major_this_host_does_not_serve_is_refused_explicitly()
	{
		var host = await StartHostAsync();
		var credential = await RawProtocolPlugin.EnrollAsync(host, PluginId);

		var (status, body) = await RawProtocolPlugin.RequestSessionAsync(host, credential, 99, 100);

		Assert.Multiple(() =>
		{
			Assert.That(status, Is.Not.EqualTo(201));
			Assert.That(body.ToJsonString(), Does.Contain("PROTOCOL_VERSION_UNSUPPORTED"));
		});
	}
}
