using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.UnitTests.Adb;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class HostStatePusherWidgetTypeTests
{
	private const string PluginId = "com.example.frames";

	private PluginSessionRegistry _registry = null!;
	private FakePluginConnection _connection = null!;
	private HostStatePusher _pusher = null!;

	[SetUp]
	public async Task SetUp()
	{
		_registry = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		_connection = new FakePluginConnection();
		_pusher = new HostStatePusher(_registry, new FakeDeckNavigator(), new FakeScriptApi(), new FakeWidgetApi(),
			new StubEventBindingTracker(), new MacroDeckHost.Application.Deck.DeckClientTracker(Serilog.Core.Logger.None),
			new FakeAdbManager(), new FixedAdbAccessPolicy(PluginAdbAccess.Available), Serilog.Core.Logger.None);

		await _registry.Create(new PluginSessionRecord
		{
			SessionId = "session-1",
			PluginId = PluginId,
			DisplayName = "Frames",
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
	public void TearDown() => _pusher.Dispose();

	[Test]
	public async Task A_widget_type_change_pushes_the_widget_list_again()
	{
		await _pusher.Handle(new WidgetTypeCatalogChangedNotification(), CancellationToken.None);

		Assert.That(await WaitForWidgetPush(), Is.True);
	}

	[Test]
	public void A_plugin_that_does_not_take_the_push_does_not_hold_up_the_widget_type_change()
	{
		_connection.SendGate = new TaskCompletionSource<bool>();

		var handled = _pusher.Handle(new WidgetTypeCatalogChangedNotification(), CancellationToken.None);

		Assert.That(handled.IsCompleted, Is.True);
		_connection.SendGate.SetResult(true);
	}

	private async Task<bool> WaitForWidgetPush()
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (DateTime.UtcNow < deadline)
		{
			var pushed = _connection.Sent.ToList()
				.Where(envelope => envelope.Type == MessageTypes.HostState)
				.Select(envelope => envelope.Payload!.Value.Deserialize<HostStatePayload>(PluginProtocolJson.Options)!)
				.Any(payload => payload.Api == HostApis.Widgets);
			if (pushed)
			{
				return true;
			}

			await Task.Delay(10);
		}

		return false;
	}
}
