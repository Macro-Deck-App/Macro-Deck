using MacroDeckHost.Integrations.HomeAssistant;

namespace MacroDeckHost.Tests.UnitTests.HomeAssistant;

[TestFixture]
internal sealed class HomeAssistantEndpointTests
{
	[Test]
	public void An_http_url_becomes_ws()
	{
		var uri = HomeAssistantEndpoint.TryBuild("http://homeassistant.local:8123");

		Assert.That(uri?.ToString(), Is.EqualTo("ws://homeassistant.local:8123/api/websocket"));
	}

	[Test]
	public void An_https_url_becomes_wss()
	{
		var uri = HomeAssistantEndpoint.TryBuild("https://my-home.ui.nabu.casa");

		Assert.That(uri?.ToString(), Is.EqualTo("wss://my-home.ui.nabu.casa/api/websocket"));
	}

	[TestCase("ws://homeassistant.local:8123")]
	[TestCase("wss://my-home.ui.nabu.casa")]
	public void A_ws_or_wss_url_passes_through_its_own_scheme(string input)
	{
		var uri = HomeAssistantEndpoint.TryBuild(input);

		Assert.That(uri?.Scheme, Is.EqualTo(input.StartsWith("wss", StringComparison.Ordinal) ? "wss" : "ws"));
	}

	[Test]
	public void The_websocket_path_is_appended_exactly_once()
	{
		var alreadyThere = HomeAssistantEndpoint.TryBuild("http://homeassistant.local:8123/api/websocket");
		var missing = HomeAssistantEndpoint.TryBuild("http://homeassistant.local:8123");

		Assert.Multiple(() =>
		{
			Assert.That(alreadyThere?.AbsolutePath, Is.EqualTo("/api/websocket"));
			Assert.That(missing?.AbsolutePath, Is.EqualTo("/api/websocket"));
		});
	}

	[Test]
	public void A_trailing_slash_is_trimmed_before_the_path_is_appended()
	{
		var uri = HomeAssistantEndpoint.TryBuild("http://homeassistant.local:8123/");

		Assert.That(uri?.AbsolutePath, Is.EqualTo("/api/websocket"));
	}

	[Test]
	public void A_reverse_proxy_subpath_is_preserved()
	{
		var uri = HomeAssistantEndpoint.TryBuild("https://example.com/home-assistant");

		Assert.That(uri?.AbsolutePath, Is.EqualTo("/home-assistant/api/websocket"));
	}

	[Test]
	public void An_explicit_port_is_preserved()
	{
		var uri = HomeAssistantEndpoint.TryBuild("http://192.168.1.50:8124");

		Assert.That(uri?.Port, Is.EqualTo(8124));
	}

	[Test]
	public void Port_8123_is_never_injected_for_a_url_without_a_port()
	{
		var local = HomeAssistantEndpoint.TryBuild("http://homeassistant.local");
		var cloud = HomeAssistantEndpoint.TryBuild("https://my-home.ui.nabu.casa");

		Assert.Multiple(() =>
		{
			Assert.That(local?.IsDefaultPort, Is.True);
			Assert.That(local?.ToString(), Does.Not.Contain("8123"));
			Assert.That(cloud?.IsDefaultPort, Is.True);
			Assert.That(cloud?.Port, Is.EqualTo(443));
		});
	}

	[Test]
	public void A_scheme_less_local_name_defaults_to_http()
	{
		var uri = HomeAssistantEndpoint.TryBuild("homeassistant.local:8123");

		Assert.That(uri?.Scheme, Is.EqualTo("ws"));
	}

	[TestCase("192.168.1.50")]
	[TestCase("localhost")]
	[TestCase("homeassistant.lan")]
	[TestCase("homeassistant.home")]
	[TestCase("homeassistant.internal")]
	public void Every_local_shaped_host_defaults_to_http(string host)
	{
		var uri = HomeAssistantEndpoint.TryBuild(host);

		Assert.That(uri?.Scheme, Is.EqualTo("ws"));
	}

	[Test]
	public void A_scheme_less_routable_name_defaults_to_https()
	{
		var uri = HomeAssistantEndpoint.TryBuild("my-home.ui.nabu.casa");

		Assert.That(uri?.Scheme, Is.EqualTo("wss"));
	}

	[Test]
	public void A_query_and_fragment_are_stripped()
	{
		var uri = HomeAssistantEndpoint.TryBuild("http://homeassistant.local:8123/lovelace/0?a=b#panel");

		Assert.That(uri?.ToString(), Is.EqualTo("ws://homeassistant.local:8123/lovelace/0/api/websocket"));
	}

	[Test]
	public void An_unsupported_scheme_is_rejected()
	{
		var uri = HomeAssistantEndpoint.TryBuild("ftp://homeassistant.local");

		Assert.That(uri, Is.Null);
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("   ")]
	[TestCase("http://")]
	public void An_unparseable_or_empty_input_answers_null(string? input)
	{
		Assert.That(HomeAssistantEndpoint.TryBuild(input), Is.Null);
	}
}
