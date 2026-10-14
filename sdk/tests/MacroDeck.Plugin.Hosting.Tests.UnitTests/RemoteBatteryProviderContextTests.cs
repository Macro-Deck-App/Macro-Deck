using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Sdk.Batteries;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class RemoteBatteryProviderContextTests
{
	private ScriptedBatteryHost _host = null!;
	private PluginConnectionState _state = null!;
	private FakePluginSocket _socket = null!;
	private RemoteBatteryProviderContext _context = null!;

	[SetUp]
	public void SetUp()
	{
		_host = new ScriptedBatteryHost();
		_state = new PluginConnectionState();
		_socket = new FakePluginSocket();
		_context = new RemoteBatteryProviderContext(_host, _state, Serilog.Core.Logger.None)
		{
			RateLimitRetryDelay = TimeSpan.FromMilliseconds(1)
		};
	}

	[TearDown]
	public async Task TearDown()
	{
		_context.Dispose();
		await _socket.DisposeAsync();
	}

	[Test]
	public async Task A_full_batch_of_devices_is_published_in_a_single_call()
	{
		Connect();
		var devices = Enumerable.Range(0, BatteryLimits.MaxDevicesPerProvider)
			.Select(index => Device($"d{index}"))
			.ToList();

		await _context.PublishAsync(devices);

		Assert.Multiple(() =>
		{
			Assert.That(_host.Calls, Has.Count.EqualTo(1));
			Assert.That(_host.Calls[0].Operation, Is.EqualTo(HostOperations.Batteries.Publish));
			Assert.That(((BatteriesPublishArguments)_host.Calls[0].Arguments).Devices,
				Has.Count.EqualTo(BatteryLimits.MaxDevicesPerProvider));
		});
	}

	[Test]
	public async Task A_publish_that_would_exceed_the_limit_is_refused_without_reaching_the_host()
	{
		Connect();
		await _context.PublishAsync([
			.. Enumerable.Range(0, BatteryLimits.MaxDevicesPerProvider).Select(index => Device($"d{index}"))
		]);

		Assert.That(() => _context.PublishAsync(Device("one-too-many")), Throws.InvalidOperationException);
		Assert.That(_host.Calls, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task Republishing_a_known_id_does_not_count_against_the_limit()
	{
		Connect();
		await _context.PublishAsync([
			.. Enumerable.Range(0, BatteryLimits.MaxDevicesPerProvider).Select(index => Device($"d{index}"))
		]);

		Assert.DoesNotThrowAsync(() => _context.PublishAsync(Device("d0", 10)));
	}

	[Test]
	public void A_batch_with_the_same_id_twice_is_refused_as_a_whole()
	{
		Connect();

		Assert.That(() => _context.PublishAsync([Device("a"), Device("b"), Device("a")]), Throws.ArgumentException);
		Assert.That(_host.Calls, Is.Empty);
	}

	[Test]
	public async Task A_refused_publish_leaves_nothing_behind_to_resend()
	{
		Connect();
		_host.Failures.Enqueue(ProtocolErrorCodes.InvalidPayload);

		Assert.That(() => _context.PublishAsync(Device("a")), Throws.InstanceOf<HostInvocationException>());

		_state.RaiseConnected(true);
		await Task.Delay(100);
		Assert.That(_host.Calls, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task Removing_a_device_sends_its_id_to_the_host()
	{
		Connect();
		await _context.PublishAsync(Device("a"));

		await _context.RemoveAsync("a");

		Assert.Multiple(() =>
		{
			Assert.That(_host.Calls[^1].Operation, Is.EqualTo(HostOperations.Batteries.Remove));
			Assert.That(((BatteriesRemoveArguments)_host.Calls[^1].Arguments).DeviceIds, Is.EqualTo(new[] { "a" }));
		});
	}

	[Test]
	public async Task A_resumed_reconnect_resends_the_whole_set_in_one_sync_call()
	{
		Connect();
		await _context.PublishAsync([Device("a"), Device("b")]);
		await _context.PublishAsync(Device("c"));
		await _context.RemoveAsync("b");

		Connect();
		_state.RaiseConnected(true);
		await WaitForAsync(() => _host.Calls.Count == 4);

		var sync = _host.Calls[^1];
		Assert.Multiple(() =>
		{
			Assert.That(sync.Operation, Is.EqualTo(HostOperations.Batteries.Sync));
			Assert.That(((BatteriesSyncArguments)sync.Arguments).Devices.Select(device => device.Id),
				Is.EquivalentTo(new[] { "a", "c" }));
		});
	}

	[Test]
	public async Task A_new_session_does_not_resend_because_the_provider_is_initialized_again()
	{
		Connect();
		await _context.PublishAsync(Device("a"));

		Connect();
		_state.RaiseConnected(false);
		await Task.Delay(100);

		Assert.That(_host.Calls, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task After_the_providers_shut_down_a_resume_has_nothing_to_resend()
	{
		Connect();
		await _context.PublishAsync(Device("a"));

		await _context.ResetAsync();
		_state.RaiseConnected(true);
		await Task.Delay(100);

		Assert.That(_host.Calls.Select(call => call.Operation), Does.Not.Contain(HostOperations.Batteries.Sync));
	}

	[Test]
	public async Task A_provider_shut_down_within_the_session_withdraws_what_it_published()
	{
		Connect();
		await _context.PublishAsync(Device("a"));

		await _context.ResetAsync();

		Assert.That(_host.Calls.Select(call => call.Operation),
			Is.EqualTo(new[] { HostOperations.Batteries.Publish, HostOperations.Batteries.Withdraw }));
	}

	[Test]
	public async Task A_provider_that_published_nothing_withdraws_nothing()
	{
		Connect();

		await _context.ResetAsync();

		Assert.That(_host.Calls, Is.Empty);
	}

	[TestCase(ProtocolErrorCodes.CapabilityUnsupported)]
	[TestCase(ProtocolErrorCodes.CapabilityUnavailable)]
	[TestCase(ProtocolErrorCodes.InternalError)]
	public async Task A_host_refusing_the_withdraw_does_not_fail_the_shutdown(string code)
	{
		Connect();
		await _context.PublishAsync(Device("a"));
		_host.Failures.Enqueue(code);

		Assert.DoesNotThrowAsync(() => _context.ResetAsync());

		_state.RaiseConnected(true);
		await Task.Delay(100);
		Assert.That(_host.Calls.Select(call => call.Operation), Does.Not.Contain(HostOperations.Batteries.Sync));
	}

	[Test]
	public async Task A_host_without_the_batteries_api_is_not_asked_to_withdraw()
	{
		Connect();
		_host.Unsupported = true;
		await _context.PublishAsync(Device("a"));

		await _context.ResetAsync();

		Assert.That(_host.Calls, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task A_publish_while_disconnected_reaches_the_host_on_the_next_resume()
	{
		await _context.PublishAsync(Device("a", 55));
		Assert.That(_host.Calls, Is.Empty);

		Connect();
		_state.RaiseConnected(true);
		await WaitForAsync(() => _host.Calls.Count == 1);

		var device = ((BatteriesSyncArguments)_host.Calls[0].Arguments).Devices.Single();
		Assert.That((device.Id, device.LevelPercent), Is.EqualTo(("a", (int?)55)));
	}

	[Test]
	public async Task A_host_without_the_batteries_api_turns_publishing_into_a_quiet_no_op()
	{
		Connect();
		_host.Unsupported = true;

		await _context.PublishAsync(Device("a"));
		await _context.PublishAsync(Device("b"));
		await _context.RemoveAsync("a");

		Assert.That(_host.Calls, Has.Count.EqualTo(1), "an unsupported host is asked once per connection");
	}

	[Test]
	public async Task A_rate_limited_publish_is_retried_once()
	{
		Connect();
		_host.Failures.Enqueue(ProtocolErrorCodes.RateLimited);

		await _context.PublishAsync(Device("a"));

		Assert.That(_host.Calls.Select(call => call.Operation),
			Is.EqualTo(new[] { HostOperations.Batteries.Publish, HostOperations.Batteries.Publish }));
	}

	[Test]
	public void A_publish_rate_limited_twice_fails()
	{
		Connect();
		_host.Failures.Enqueue(ProtocolErrorCodes.RateLimited);
		_host.Failures.Enqueue(ProtocolErrorCodes.RateLimited);

		Assert.That(() => _context.PublishAsync(Device("a")),
			Throws.InstanceOf<HostInvocationException>()
				.With.Property(nameof(HostInvocationException.Code))
				.EqualTo(ProtocolErrorCodes.RateLimited));
	}

	private static BatteryDevice Device(string id, int? level = 80)
	{
		return new BatteryDevice
			{ Id = id, Name = id.ToUpperInvariant(), Kind = BatteryDeviceKinds.Headphones, LevelPercent = level };
	}

	private void Connect()
	{
		_state.ActiveConnection = new PluginSessionConnection(_socket,
			TestSession.Create(),
			TestSession.Dispatcher(),
			_state,
			TimeProvider.System,
			Serilog.Core.Logger.None,
			null,
			null);
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

	private sealed class ScriptedBatteryHost : IHostInvoker
	{
		private readonly ConcurrentQueue<(string Operation, object Arguments)> _calls = new();

		public IReadOnlyList<(string Operation, object Arguments)> Calls => [.. _calls];

		public Queue<string> Failures { get; } = new();

		public bool Unsupported { get; set; }

		public Task<JsonElement?> InvokeAsync(
			string api,
			string operation,
			object? arguments,
			CancellationToken cancellationToken)
		{
			Assert.That(api, Is.EqualTo(HostApis.Batteries));
			_calls.Enqueue((operation, arguments!));

			if (Unsupported)
			{
				throw HostInvocationException.From(new ProtocolError
				{
					Code = ProtocolErrorCodes.CapabilityUnsupported, Message = "No batteries api.", Retryable = false
				});
			}

			if (Failures.TryDequeue(out var code))
			{
				throw HostInvocationException.From(new ProtocolError
					{ Code = code, Message = code, Retryable = false });
			}

			return Task.FromResult<JsonElement?>(null);
		}

		public bool TryComplete(ProtocolEnvelope result)
		{
			return false;
		}
	}
}
