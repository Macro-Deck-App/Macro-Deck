using MacroDeck.Plugin.Protocol.Versioning;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.UnitTests.Adb;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class HostStatePusherAdbTests
{
	private const string PluginId = "com.example.android";

	private PluginSessionRegistry _registry = null!;
	private FakePluginConnection _connection = null!;
	private FakeAdbManager _adb = null!;
	private FixedAdbAccessPolicy _access = null!;
	private HostStatePusher _pusher = null!;

	[SetUp]
	public async Task SetUp()
	{
		_registry = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		_connection = new FakePluginConnection();
		_adb = new FakeAdbManager
		{
			Devices =
			[
				Device("A1", AdbDeviceState.Device),
				Device("B2", AdbDeviceState.Authorizing),
				Device("GONE", AdbDeviceState.Disconnected)
			]
		};
		_access = new FixedAdbAccessPolicy(PluginAdbAccess.Available);
		_pusher = new HostStatePusher(_registry, new FakeDeckNavigator(), new FakeScriptApi(), new FakeWidgetApi(),
			new StubEventBindingTracker(), new MacroDeckHost.Application.Deck.DeckClientTracker(Serilog.Core.Logger.None),
			_adb, _access, Serilog.Core.Logger.None);

		await _registry.Create(new PluginSessionRecord
		{
			SessionId = "session-1",
			PluginId = PluginId,
			DisplayName = "Android",
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
	public async Task Registration_pushes_the_plugins_access_and_the_attached_devices()
	{
		await _pusher.PushAllAsync(PluginId);

		var push = AdbPushes().Single();
		Assert.Multiple(() =>
		{
			Assert.That(push.Access, Is.EqualTo(AdbAccessStates.Available));
			Assert.That(push.Devices.Select(device => (device.Serial, device.State)),
				Is.EqualTo(new[] { ("A1", AdbDeviceStates.Online), ("B2", AdbDeviceStates.Connecting) }));
		});
	}

	[Test]
	public async Task A_plugin_without_access_is_told_why_and_sees_no_devices()
	{
		_access.Access = PluginAdbAccess.NotAllowed;

		await _pusher.PushAllAsync(PluginId);

		var push = AdbPushes().Single();
		Assert.Multiple(() =>
		{
			Assert.That(push.Access, Is.EqualTo(AdbAccessStates.NotAllowed));
			Assert.That(push.Devices, Is.Empty);
		});
	}

	[Test]
	public async Task A_change_in_what_adb_sees_is_pushed_again_with_a_higher_revision()
	{
		await _pusher.PushAllAsync(PluginId);
		_adb.Devices = [];

		_adb.RaiseSnapshotChanged();

		var pushes = await WaitForAdbPushes(2);
		Assert.Multiple(() =>
		{
			Assert.That(pushes[1].Devices, Is.Empty);
			Assert.That(pushes[1].Revision, Is.GreaterThan(pushes[0].Revision));
		});
	}

	[Test]
	public async Task Changing_the_plugin_access_setting_refreshes_the_policy_and_pushes_again()
	{
		await _pusher.PushAllAsync(PluginId);
		_access.Access = PluginAdbAccess.NotAllowed;
		var settings = new AdbSettings(true, null, true, null, AllowPlugins: false);

		await _pusher.Handle(new AdbSettingsChangedNotification(settings), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_access.Refreshed, Is.EqualTo(new[] { settings }));
			Assert.That(AdbPushes()[^1].Access, Is.EqualTo(AdbAccessStates.NotAllowed));
		});
	}

	private async Task<List<AdbStateDto>> WaitForAdbPushes(int count)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (DateTime.UtcNow < deadline && AdbPushes().Count < count)
		{
			await Task.Delay(10);
		}

		return AdbPushes();
	}

	private List<AdbStateDto> AdbPushes()
		=> _connection.Sent.ToList()
			.Where(envelope => envelope.Type == MessageTypes.HostState)
			.Select(envelope => envelope.Payload!.Value.Deserialize<HostStatePayload>(PluginProtocolJson.Options)!)
			.Where(payload => payload.Api == HostApis.Adb)
			.Select(payload => payload.Data!.Value.Deserialize<AdbStateDto>(PluginProtocolJson.Options)!)
			.ToList();

	private static AdbDevice Device(string serial, AdbDeviceState state)
		=> new(serial, state, "Pixel 8", "Google", "shiba", "1", null, DateTimeOffset.UtcNow);
}
