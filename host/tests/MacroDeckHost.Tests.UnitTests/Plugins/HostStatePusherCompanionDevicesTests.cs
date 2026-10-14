using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk.Companion;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.UnitTests.Adb;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class HostStatePusherCompanionDevicesTests
{
	private const string PluginId = "com.example.phones";
	private static readonly Guid _phone = Guid.Parse("44444444-4444-4444-4444-444444444444");

	private PluginSessionRegistry _registry = null!;
	private FakePluginConnection _connection = null!;
	private ScriptedCompanionDevices _devices = null!;
	private SwitchableAccess _access = null!;
	private HostStatePusher _pusher = null!;

	[SetUp]
	public async Task SetUp()
	{
		_registry = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		_connection = new FakePluginConnection();
		_devices = new ScriptedCompanionDevices();
		_access = new SwitchableAccess();
		_pusher = new HostStatePusher(_registry,
			new FakeDeckNavigator(),
			new FakeScriptApi(),
			new FakeWidgetApi(),
			new StubEventBindingTracker(),
			new MacroDeckHost.Application.Deck.DeckClientTracker(Serilog.Core.Logger.None),
			new FakeAdbManager(),
			new FixedAdbAccessPolicy(PluginAdbAccess.NotEnabled),
			Serilog.Core.Logger.None,
			companionDevices: _devices,
			companionAccess: _access);

		await _registry.Create(new PluginSessionRecord
		{
			SessionId = "session-1",
			PluginId = PluginId,
			DisplayName = "Phones",
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
	public async Task A_plugin_that_may_not_use_companion_devices_is_told_so_once_and_sees_no_device()
	{
		_devices.Connect(_phone, new CompanionDeviceState { BatteryLevelPercent = 50 });

		await _pusher.PushAllAsync(PluginId);
		_devices.Report(_phone, new CompanionDeviceState { BatteryLevelPercent = 49 });
		await Task.Delay(50);

		var pushes = Pushes();
		Assert.Multiple(() =>
		{
			Assert.That(pushes, Has.Count.EqualTo(1));
			Assert.That(pushes[0].Access, Is.EqualTo(CompanionDeviceAccessStates.NotAllowed));
			Assert.That(pushes[0].Devices, Is.Empty);
		});
	}

	[Test]
	public async Task Allowing_plugins_pushes_the_devices_and_each_report_after_it_with_a_higher_revision()
	{
		_devices.Connect(_phone, new CompanionDeviceState { BatteryLevelPercent = 50 });
		await _pusher.PushAllAsync(PluginId);

		_access.Allow();
		await WaitForPushesAsync(2);
		_devices.Report(_phone, new CompanionDeviceState { BatteryLevelPercent = 49 });
		await WaitForPushesAsync(3);

		var pushes = Pushes();
		Assert.Multiple(() =>
		{
			Assert.That(pushes.Select(push => push.Access),
				Is.EqualTo(new[] { "not-allowed", "available", "available" }));
			Assert.That(pushes[2].Devices.Single().BatteryLevelPercent, Is.EqualTo(49));
			Assert.That(pushes.Select(push => push.Revision), Is.Ordered.Ascending);
			Assert.That(pushes.Select(push => push.Revision), Is.Unique);
		});
	}

	private async Task WaitForPushesAsync(int count)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (DateTime.UtcNow < deadline && Pushes().Count < count)
		{
			await Task.Delay(10);
		}
	}

	private List<CompanionDevicesStateDto> Pushes()
	{
		return _connection.Sent.ToList()
			.Where(envelope => envelope.Type == MessageTypes.HostState)
			.Select(envelope => envelope.Payload!.Value.Deserialize<HostStatePayload>(PluginProtocolJson.Options)!)
			.Where(payload => payload.Api == HostApis.CompanionDevices)
			.Select(payload => payload.Data!.Value.Deserialize<CompanionDevicesStateDto>(PluginProtocolJson.Options)!)
			.ToList();
	}

	private sealed class SwitchableAccess : IPluginCompanionDeviceAccessPolicy
	{
		private bool _allowed;

		public event Action? Changed;

		public void Allow()
		{
			_allowed = true;
			Changed?.Invoke();
		}

		public Task<PluginCompanionDeviceAccess> EvaluateAsync(
			string pluginId,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult(_allowed
				? PluginCompanionDeviceAccess.Available
				: PluginCompanionDeviceAccess.NotAllowed);
		}

		public bool CanBeGranted(string pluginId)
		{
			return true;
		}

		public Task<bool> GetAllowPluginsAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult(_allowed);
		}

		public Task SetAllowPluginsAsync(bool allow, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}
	}
}
