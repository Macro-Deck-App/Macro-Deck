using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Batteries;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class RemoteBatteryApiTests
{
	private HostStateCache _cache = null!;
	private RemoteBatteryApi _api = null!;

	[SetUp]
	public void SetUp()
	{
		_cache = new HostStateCache(new PluginConnectionState());
		_api = new RemoteBatteryApi(_cache, Serilog.Core.Logger.None);
	}

	[TearDown]
	public void TearDown()
	{
		_api.Dispose();
	}

	[Test]
	public void Before_the_first_push_there_are_no_devices()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_api.Devices, Is.Empty);
			Assert.That(_api.Find("p:a"), Is.Null);
		});
	}

	[Test]
	public async Task A_push_replaces_the_devices_and_raises_changed_with_its_removed_ids()
	{
		var raised = new ConcurrentQueue<BatteryDevicesChangedEventArgs>();
		_api.Changed += (_, args) => raised.Enqueue(args);

		Push(1, [Snapshot("adb", "pixel", 64), Snapshot("com.example.buds", "left", 20)]);
		Push(2, [Snapshot("adb", "pixel", 63)], ["com.example.buds:left"]);
		await WaitForAsync(() => raised.Count == 2);

		var pixel = _api.Find("adb:pixel");
		Assert.Multiple(() =>
		{
			Assert.That(_api.Devices.Select(device => device.Id), Is.EqualTo(new[] { "adb:pixel" }));
			Assert.That(pixel!.LevelPercent, Is.EqualTo(63));
			Assert.That(pixel.ProviderId, Is.EqualTo("adb"));
			Assert.That(pixel.LocalId, Is.EqualTo("pixel"));
			Assert.That(raised.Select(args => args.RemovedIds),
				Is.EqualTo(new[] { Array.Empty<string>(), ["com.example.buds:left"] }));
		});
	}

	[Test]
	public async Task A_stale_push_is_ignored()
	{
		var raised = 0;
		_api.Changed += (_, _) => Interlocked.Increment(ref raised);

		Push(2, [Snapshot("adb", "pixel", 50)]);
		Push(1, [Snapshot("adb", "pixel", 90)]);
		await WaitForAsync(() => Volatile.Read(ref raised) == 1);
		await Task.Delay(50);

		Assert.Multiple(() =>
		{
			Assert.That(_api.Find("adb:pixel")!.LevelPercent, Is.EqualTo(50));
			Assert.That(Volatile.Read(ref raised), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Initializing_again_releases_the_integrations_own_handlers_but_not_injected_ones()
	{
		var lifecycleCalls = 0;
		var injectedCalls = 0;
		_api.Lifecycle.Changed += (_, _) => Interlocked.Increment(ref lifecycleCalls);
		_api.Changed += (_, _) => Interlocked.Increment(ref injectedCalls);

		_api.ReleaseLifecycleHandlers();
		Push(1, [Snapshot("adb", "pixel", 50)]);
		await WaitForAsync(() => Volatile.Read(ref injectedCalls) == 1);
		await Task.Delay(50);

		Assert.That(Volatile.Read(ref lifecycleCalls), Is.Zero);
	}

	[Test]
	public async Task A_throwing_handler_does_not_keep_the_others_from_running()
	{
		var called = false;
		_api.Changed += (_, _) => throw new InvalidOperationException("boom");
		_api.Changed += (_, _) => called = true;

		Push(1, [Snapshot("adb", "pixel", 50)]);

		await WaitForAsync(() => called);
	}

	[Test]
	public void A_host_state_for_an_api_this_sdk_does_not_know_is_stored_without_raising_anything()
	{
		var raised = 0;
		var cache = new HostStateCache(new PluginConnectionState());
		cache.ConfigChanged += () => raised++;
		cache.DeckChanged += () => raised++;
		cache.AdbChanged += () => raised++;
		cache.EventBindingsChanged += () => raised++;
		cache.ColorsChanged += () => raised++;
		cache.BatteriesChanged += () => raised++;

		Assert.DoesNotThrow(()
			=> cache.Apply(StatePush("some-future-api", new { revision = 1, devices = new[] { new { id = "x" } } })));
		Assert.Multiple(() =>
		{
			Assert.That(raised, Is.Zero);
			Assert.That(cache.GetList<string>(HostApis.Scripts), Is.Empty);
		});
	}

	private static BatteryDeviceSnapshotDto Snapshot(string provider, string localId, int level)
	{
		return new BatteryDeviceSnapshotDto
		{
			Id = $"{provider}:{localId}",
			LocalId = localId,
			ProviderId = provider,
			ProviderName = provider,
			Name = localId,
			Kind = BatteryDeviceKinds.Phone,
			LevelPercent = level,
			ChargeState = BatteryChargeStates.Discharging,
			IsConnected = true,
			UpdatedAt = DateTimeOffset.UnixEpoch
		};
	}

	private void Push(
		long revision,
		IReadOnlyList<BatteryDeviceSnapshotDto> devices,
		IReadOnlyList<string>? removedIds = null)
	{
		_cache.Apply(StatePush(HostApis.Batteries,
			new BatteryStateDto { Revision = revision, Devices = devices, RemovedIds = removedIds ?? [] }));
	}

	private static ProtocolEnvelope StatePush(string api, object data)
	{
		return new ProtocolEnvelope
		{
			Type = MessageTypes.HostState,
			Id = Guid.NewGuid().ToString(),
			Payload = JsonSerializer.SerializeToElement(new HostStatePayload
				{
					Api = api, Data = JsonSerializer.SerializeToElement(data, PluginProtocolJson.Options)
				},
				PluginProtocolJson.Options)
		};
	}

	private static async Task WaitForAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail("The expected state was never reached.");
			}

			await Task.Delay(10);
		}
	}
}
