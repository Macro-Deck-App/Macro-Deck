using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Network.Http;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.UnitTests.Adb;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class HostStatePusherHttpTests
{
	private const string PluginId = "com.example.http";

	private PluginSessionRegistry _registry = null!;
	private FakePluginConnection _connection = null!;
	private HttpUserAgentState _userAgent = null!;
	private HostStatePusher _pusher = null!;

	[SetUp]
	public async Task SetUp()
	{
		_registry = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		_connection = new FakePluginConnection();
		_userAgent = new HttpUserAgentState();
		_pusher = new HostStatePusher(_registry,
			new FakeDeckNavigator(),
			new FakeScriptApi(),
			new FakeWidgetApi(),
			new StubEventBindingTracker(),
			new MacroDeckHost.Application.Deck.DeckClientTracker(Serilog.Core.Logger.None),
			new FakeAdbManager(),
			new FixedAdbAccessPolicy(PluginAdbAccess.NotEnabled),
			Serilog.Core.Logger.None,
			userAgent: _userAgent);

		await _registry.Create(new PluginSessionRecord
		{
			SessionId = "session-1",
			PluginId = PluginId,
			DisplayName = "Http",
			Origin = PluginSessionOrigin.Managed,
			NegotiatedVersion = 1,
			Capabilities = new Dictionary<string, CapabilityNegotiationResult>(StringComparer.Ordinal),
			DeclaredCapabilities = [],
			State = PluginSessionState.Awaiting,
			CreatedAt = TimeProvider.System.GetUtcNow()
		});
		_registry.TryAttach("session-1", _connection, null);
	}

	[TearDown]
	public void TearDown()
	{
		_pusher.Dispose();
	}

	[Test]
	public async Task Registration_pushes_the_default_user_agent_when_none_is_configured()
	{
		await _pusher.PushAllAsync(PluginId);

		Assert.That(HttpPushes().Single().UserAgent, Is.EqualTo(HttpUserAgent.Default));
	}

	[Test]
	public async Task A_changed_user_agent_reaches_a_connected_plugin_without_a_reconnect()
	{
		await _pusher.PushAllAsync(PluginId);

		_userAgent.Apply("Custom/2.0");

		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (DateTime.UtcNow < deadline && HttpPushes().Count < 2)
		{
			await Task.Delay(10);
		}

		Assert.That(HttpPushes().Select(push => push.UserAgent),
			Is.EqualTo(new[] { HttpUserAgent.Default, "Custom/2.0" }));
	}

	private List<HttpStateDto> HttpPushes()
	{
		return _connection.Sent.ToList()
			.Where(envelope => envelope.Type == MessageTypes.HostState)
			.Select(envelope => envelope.Payload!.Value.Deserialize<HostStatePayload>(PluginProtocolJson.Options)!)
			.Where(payload => payload.Api == HostApis.Http)
			.Select(payload => payload.Data!.Value.Deserialize<HttpStateDto>(PluginProtocolJson.Options)!)
			.ToList();
	}
}
