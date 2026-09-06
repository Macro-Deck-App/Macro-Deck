using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk.Devices;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Devices;

/// <summary>
/// A plugin that is not connected cannot serve its hardware, so its devices have to stop claiming to be
/// reachable - while staying registered, so reconnecting is a reuse rather than a new device.
/// </summary>
public class PluginDeviceSessionWatcherTests
{
	private const string PluginId = "com.example.deck";

	private PluginSessionRegistry _sessions = null!;
	private FakePluginDeviceRegistry _devices = null!;
	private PluginDeviceSessionWatcher _watcher = null!;

	[SetUp]
	public async Task SetUp()
	{
		_sessions = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		_devices = new FakePluginDeviceRegistry();
		_watcher = new PluginDeviceSessionWatcher(_sessions, _devices, Serilog.Core.Logger.None);

		await _watcher.StartAsync(CancellationToken.None);
		await _devices.RegisterAsync(PluginId, new DeviceDescriptor("SERIAL-1", "Deck"));
	}

	private async Task<string> OpenSessionAsync()
	{
		var record = new PluginSessionRecord
		{
			SessionId = Guid.CreateVersion7().ToString("D"),
			PluginId = PluginId,
			DisplayName = "Example Plugin",
			Origin = PluginSessionOrigin.Managed,
			NegotiatedVersion = 1,
			Capabilities = new Dictionary<string, CapabilityNegotiationResult>(StringComparer.Ordinal),
			DeclaredCapabilities = [],
			State = PluginSessionState.Awaiting,
			CreatedAt = TimeProvider.System.GetUtcNow()
		};

		await _sessions.Create(record);
		return record.SessionId;
	}

	[Test]
	public async Task A_terminated_session_takes_the_plugins_devices_offline_but_keeps_them()
	{
		var sessionId = await OpenSessionAsync();

		_sessions.MakeNonResumable(sessionId);
		await _sessions.Terminate(sessionId, 1000, "test");
		await WaitForAsync(() => !_devices.IsOnline(PluginId, "SERIAL-1"));

		Assert.Multiple(() =>
		{
			Assert.That(_devices.IsOnline(PluginId, "SERIAL-1"), Is.False);
			Assert.That(_devices.AssignedIdOf(PluginId, "SERIAL-1"),
				Is.Not.Null,
				"the device is retained so a reconnecting plugin re-registers the same one");
		});
	}

	[Test]
	public async Task A_stopped_watcher_leaves_the_devices_alone()
	{
		var sessionId = await OpenSessionAsync();
		await _watcher.StopAsync(CancellationToken.None);

		_sessions.MakeNonResumable(sessionId);
		await _sessions.Terminate(sessionId, 1000, "test");
		await Task.Delay(50);

		Assert.That(_devices.IsOnline(PluginId, "SERIAL-1"), Is.True);
	}

	private static async Task WaitForAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
		while (DateTime.UtcNow < deadline && !condition())
		{
			await Task.Delay(5);
		}
	}
}
