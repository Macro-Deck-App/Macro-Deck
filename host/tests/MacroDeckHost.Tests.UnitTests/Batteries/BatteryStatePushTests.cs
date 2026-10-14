using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk.Batteries;
using MacroDeckHost.Application.Batteries;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.UnitTests.Adb;
using MacroDeckHost.Tests.UnitTests.Plugins;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Batteries;

[TestFixture]
internal sealed class BatteryStatePushTests
{
	private const string ReaderId = "com.example.reader";
	private const string ProviderId = "com.example.provider";

	private PluginSessionRegistry _sessions = null!;
	private FakePluginConnection _reader = null!;
	private BatteryRegistry _batteries = null!;
	private HostStatePusher _pusher = null!;

	[SetUp]
	public async Task SetUp()
	{
		_sessions = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		_batteries = new BatteryRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		_pusher = new HostStatePusher(_sessions,
			new FakeDeckNavigator(),
			new FakeScriptApi(),
			new FakeWidgetApi(),
			new StubEventBindingTracker(),
			new MacroDeckHost.Application.Deck.DeckClientTracker(Serilog.Core.Logger.None),
			new FakeAdbManager(),
			new FixedAdbAccessPolicy(),
			Serilog.Core.Logger.None,
			_batteries);

		_reader = new FakePluginConnection();
		await OpenSessionAsync(ReaderId, "reader-session", _reader);
	}

	[TearDown]
	public void TearDown()
	{
		_pusher.Dispose();
		_batteries.Dispose();
	}

	[Test]
	public async Task A_connecting_plugin_receives_every_providers_devices()
	{
		_batteries.SetProviderName(ProviderId, "Provider");
		_batteries.Publish(ProviderId, null, [new BatteryDevice { Id = "phone", Name = "Phone", LevelPercent = 70 }]);

		await _pusher.PushAllAsync(ReaderId);

		var push = BatteryPushes()[0];
		Assert.Multiple(() =>
		{
			Assert.That(push.Devices.Select(device => (device.Id, device.ProviderName, device.LevelPercent)),
				Is.EqualTo(new[] { ($"{ProviderId}:phone", "Provider", (int?)70) }));
			Assert.That(push.RemovedIds, Is.Empty);
		});
	}

	[Test]
	public async Task A_change_is_pushed_again_with_a_higher_revision_and_its_removals()
	{
		_batteries.Publish(ProviderId, null, [new BatteryDevice { Id = "phone", Name = "Phone" }]);
		await _pusher.PushAllAsync(ReaderId);

		_batteries.Remove(ProviderId, ["phone"]);
		var pushes = await WaitForPushesAsync(push => push.RemovedIds.Count > 0);

		var first = pushes[0];
		var removal = pushes.Last(push => push.RemovedIds.Count > 0);
		Assert.Multiple(() =>
		{
			Assert.That(removal.Devices, Is.Empty);
			Assert.That(removal.RemovedIds, Is.EqualTo(new[] { $"{ProviderId}:phone" }));
			Assert.That(removal.Revision, Is.GreaterThan(first.Revision));
		});
	}

	[Test]
	public async Task A_plugin_whose_send_fails_does_not_keep_the_change_from_later_plugins()
	{
		var broken = new FakePluginConnection("broken-connection") { SendGate = new TaskCompletionSource<bool>() };
		broken.SendGate.SetException(new IOException("The socket is gone."));
		await OpenSessionAsync("com.example.broken", "broken-session", broken);
		var lateReader = new FakePluginConnection("late-connection");
		await OpenSessionAsync("com.example.late-reader", "late-session", lateReader);
		await _pusher.PushAllAsync(ReaderId);

		_batteries.Publish(ProviderId, null, [new BatteryDevice { Id = "phone", Name = "Phone" }]);

		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (DateTime.UtcNow < deadline && BatteryPushes(lateReader).Count == 0)
		{
			await Task.Delay(10);
		}

		Assert.Multiple(() =>
		{
			Assert.That(broken.Sent, Is.Not.Empty, "the failing plugin was attempted");
			Assert.That(BatteryPushes(lateReader).Single().Devices.Select(device => device.Id),
				Is.EqualTo(new[] { $"{ProviderId}:phone" }));
		});
	}

	[Test]
	public async Task An_ended_plugin_session_withdraws_what_it_published_but_not_a_newer_sessions_devices()
	{
		var watcher = new PluginBatterySessionWatcher(_sessions, _batteries, Serilog.Core.Logger.None);
		await watcher.StartAsync(CancellationToken.None);
		await OpenSessionAsync(ProviderId, "old-session", new FakePluginConnection());
		_batteries.Publish(ProviderId, "old-session", [new BatteryDevice { Id = "phone", Name = "Phone" }]);
		_batteries.Publish(ProviderId, "new-session", [new BatteryDevice { Id = "watch", Name = "Watch" }]);

		_sessions.MakeNonResumable("old-session");
		await _sessions.Terminate("old-session", 1000, "test");
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (DateTime.UtcNow < deadline && _batteries.Devices.Count > 1)
		{
			await Task.Delay(10);
		}

		await watcher.StopAsync(CancellationToken.None);

		Assert.That(_batteries.Devices.Select(device => device.LocalId), Is.EqualTo(new[] { "watch" }));
	}

	private async Task OpenSessionAsync(string pluginId, string sessionId, FakePluginConnection connection)
	{
		await _sessions.Create(new PluginSessionRecord
		{
			SessionId = sessionId,
			PluginId = pluginId,
			DisplayName = pluginId,
			Origin = PluginSessionOrigin.Managed,
			NegotiatedVersion = 1,
			Capabilities = new Dictionary<string, CapabilityNegotiationResult>(StringComparer.Ordinal),
			DeclaredCapabilities = [],
			State = PluginSessionState.Awaiting,
			CreatedAt = TimeProvider.System.GetUtcNow()
		});
		_sessions.TryAttach(sessionId, connection, null);
	}

	private async Task<List<BatteryStateDto>> WaitForPushesAsync(Func<BatteryStateDto, bool> until)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (DateTime.UtcNow < deadline && !BatteryPushes().Any(until))
		{
			await Task.Delay(10);
		}

		return BatteryPushes();
	}

	private List<BatteryStateDto> BatteryPushes()
	{
		return BatteryPushes(_reader);
	}

	private static List<BatteryStateDto> BatteryPushes(FakePluginConnection connection)
	{
		return connection.Sent.ToList()
			.Where(envelope => envelope.Type == MessageTypes.HostState)
			.Select(envelope => envelope.Payload!.Value.Deserialize<HostStatePayload>(PluginProtocolJson.Options)!)
			.Where(payload => payload.Api == HostApis.Batteries)
			.Select(payload => payload.Data!.Value.Deserialize<BatteryStateDto>(PluginProtocolJson.Options)!)
			.ToList();
	}
}
