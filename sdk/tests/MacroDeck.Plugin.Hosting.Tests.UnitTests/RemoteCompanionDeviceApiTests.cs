using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Companion;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class RemoteCompanionDeviceApiTests
{
	private static readonly Guid _phone = Guid.Parse("11111111-1111-1111-1111-111111111111");
	private static readonly Guid _tablet = Guid.Parse("22222222-2222-2222-2222-222222222222");

	private RemoteIntegrationContextTests.RecordingHostInvoker _invoker = null!;
	private HostStateCache _cache = null!;
	private RemoteCompanionDeviceApi _api = null!;
	private ConcurrentQueue<string> _events = null!;

	[SetUp]
	public void SetUp()
	{
		_invoker = new RemoteIntegrationContextTests.RecordingHostInvoker();
		_cache = new HostStateCache(new PluginConnectionState());
		_api = new RemoteCompanionDeviceApi(_invoker, _cache, Serilog.Core.Logger.None);
		// Events are raised off the caller's thread, so a previous test's late ones must not reach this queue.
		var events = _events = new ConcurrentQueue<string>();
		_api.AccessChanged += (_, _) => events.Enqueue("access");
		_api.DeviceReady += (_, e) => events.Enqueue($"ready:{Name(e.DeviceId)}:{e.Device!.ReadySequence}");
		_api.DeviceChanged += (_, e)
			=> events.Enqueue($"changed:{Name(e.DeviceId)}:{(e.Device is null ? "gone" : "here")}");
	}

	[TearDown]
	public void TearDown()
	{
		_api.Dispose();
		_invoker.Dispose();
	}

	[Test]
	public async Task Before_the_host_reports_anything_access_is_unsupported_and_operations_do_not_reach_the_host()
	{
		var sent = await _api.SendAsync(_phone, CompanionDeviceCommand.Vibrate);
		var requested = await _api.RequestAsync(_phone, CompanionDeviceCommand.ScreenOn);

		Assert.Multiple(() =>
		{
			Assert.That(_api.Access, Is.EqualTo(CompanionDeviceAccess.Unsupported));
			Assert.That(_api.Devices, Is.Empty);
			Assert.That(sent, Is.False);
			Assert.That(requested,
				Is.EqualTo(CompanionDeviceCommandResult.Failed(CompanionDeviceFailure.NotConnected)));
			Assert.That(_invoker.LastApi, Is.Null);
		});
	}

	[Test]
	public async Task A_push_lists_the_devices_with_their_state_and_reports_each_as_ready()
	{
		Push(1, CompanionDeviceAccessStates.Available, Device(_phone, 1, 80), Device(_tablet, 2));

		await WaitForEventsAsync(5);
		Assert.Multiple(() =>
		{
			Assert.That(_api.Access, Is.EqualTo(CompanionDeviceAccess.Available));
			Assert.That(_api.Devices.Select(device => device.Id), Is.EquivalentTo(new[] { _phone, _tablet }));
			Assert.That(_api.FindDevice(_phone)!.State.BatteryLevelPercent, Is.EqualTo(80));
			Assert.That(_events,
				Is.EqualTo(new[]
				{
					"access", "changed:phone:here", "ready:phone:1", "changed:tablet:here", "ready:tablet:2"
				}));
		});
	}

	[Test]
	public async Task A_new_report_is_a_change_a_new_connection_is_ready_again_and_a_device_that_left_is_gone()
	{
		Push(1, CompanionDeviceAccessStates.Available, Device(_phone, 1, 80), Device(_tablet, 2));
		await WaitForEventsAsync(5);
		_events.Clear();

		Push(2, CompanionDeviceAccessStates.Available, Device(_phone, 1, 75));
		Push(3, CompanionDeviceAccessStates.Available, Device(_phone, 1, 75));
		Push(4, CompanionDeviceAccessStates.Available, Device(_phone, 3, 75));

		await WaitForEventsAsync(4);
		Assert.That(_events,
			Is.EqualTo(new[]
			{
				"changed:phone:here", "changed:tablet:gone", "changed:phone:here", "ready:phone:3"
			}));
	}

	[Test]
	public async Task A_plugin_that_is_not_allowed_sees_no_devices_and_its_operations_report_it()
	{
		Push(1, CompanionDeviceAccessStates.NotAllowed, Device(_phone, 1));
		var api = new RemoteCompanionDeviceApi(new FailingHostInvoker(new ProtocolError
			{
				Code = ProtocolErrorCodes.CompanionDevicesNotAllowed, Message = "not allowed", Retryable = false
			}),
			_cache,
			Serilog.Core.Logger.None,
			new ScriptedHostAssets());

		var requested = await api.RequestAsync(_phone, CompanionDeviceCommand.ScreenOn);
		var screenshot = await api.CaptureScreenshotAsync(_phone, CompanionScreenshotMode.Deck);
		var sent = await api.SendAsync(_phone, CompanionDeviceCommand.Vibrate);

		Assert.Multiple(() =>
		{
			Assert.That(api.Access, Is.EqualTo(CompanionDeviceAccess.NotAllowed));
			Assert.That(api.Devices, Is.Empty);
			Assert.That(requested, Is.EqualTo(CompanionDeviceCommandResult.Failed(CompanionDeviceFailure.NotAllowed)));
			Assert.That(screenshot.Failure, Is.EqualTo(CompanionDeviceFailure.NotAllowed));
			Assert.That(sent, Is.False);
		});
		api.Dispose();
	}

	[Test]
	public async Task A_request_sends_the_command_and_reads_the_devices_answer()
	{
		Push(1, CompanionDeviceAccessStates.Available, Device(_phone, 1));
		_invoker.NextResult = Json(new CompanionDeviceRequestResultDto
		{
			Outcome = CompanionDeviceOutcomes.Failed, Failure = CompanionDeviceFailures.ConsentDenied
		});

		var result = await _api.RequestAsync(_phone, CompanionDeviceCommand.SetBrightness(40));

		var arguments = (CompanionDeviceCommandArguments)_invoker.LastArguments!;
		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(CompanionDeviceCommandResult.Failed(CompanionDeviceFailure.ConsentDenied)));
			Assert.That((_invoker.LastApi, _invoker.LastOperation),
				Is.EqualTo((HostApis.CompanionDevices, HostOperations.CompanionDevices.Request)));
			Assert.That((arguments.DeviceId, arguments.Command, arguments.BrightnessPercent),
				Is.EqualTo((_phone.ToString("D"), CompanionDeviceCommands.SetBrightness, (int?)40)));
		});
	}

	[Test]
	public async Task A_send_answers_whether_the_host_reached_the_device()
	{
		Push(1, CompanionDeviceAccessStates.Available, Device(_phone, 1));
		_invoker.NextResult = Json(new CompanionDeviceSendResultDto { Sent = true });

		Assert.That(await _api.SendAsync(_phone, CompanionDeviceCommand.Vibrate), Is.True);
		Assert.That(_invoker.LastOperation, Is.EqualTo(HostOperations.CompanionDevices.Send));
	}

	[Test]
	public async Task A_screenshot_arrives_over_the_host_asset_channel_named_in_the_reply()
	{
		var assets = new ScriptedHostAssets();
		var api = new RemoteCompanionDeviceApi(_invoker, _cache, Serilog.Core.Logger.None, assets);
		Push(1, CompanionDeviceAccessStates.Available, Device(_phone, 1));
		byte[] png = [137, 80, 78, 71];
		assets.Transfers["transfer-1"] = new HostAssetTransfer(true, "image/png", png);
		_invoker.NextResult = Json(new CompanionScreenshotResultDto
		{
			ContentType = "image/png", ByteLength = png.Length, ContentHash = "sha256:x", TransferId = "transfer-1"
		});

		var result = await api.CaptureScreenshotAsync(_phone, CompanionScreenshotMode.FullScreen);

		Assert.Multiple(() =>
		{
			Assert.That(result.Png, Is.EqualTo(png));
			Assert.That(((CompanionScreenshotArguments)_invoker.LastArguments!).Mode,
				Is.EqualTo(CompanionScreenshotModes.Full));
		});
		api.Dispose();
	}

	[Test]
	public async Task A_screenshot_the_device_refused_carries_its_failure()
	{
		var api = new RemoteCompanionDeviceApi(_invoker, _cache, Serilog.Core.Logger.None, new ScriptedHostAssets());
		Push(1, CompanionDeviceAccessStates.Available, Device(_phone, 1));
		_invoker.NextResult = Json(new CompanionScreenshotResultDto
			{ Failure = CompanionDeviceFailures.ConsentDenied });

		var result = await api.CaptureScreenshotAsync(_phone, CompanionScreenshotMode.FullScreen);

		Assert.That(result.Failure, Is.EqualTo(CompanionDeviceFailure.ConsentDenied));
		api.Dispose();
	}

	[Test]
	public async Task Waiting_for_a_device_the_host_reports_ready_lasts_until_its_state_arrives()
	{
		Push(1, CompanionDeviceAccessStates.Available);
		_invoker.NextResult = Json(new CompanionDeviceWaitResultDto { Ready = true });

		var waiting = _api.WaitForDeviceAsync(_phone);
		await _invoker.WaitForCallAsync();
		Push(2, CompanionDeviceAccessStates.Available, Device(_phone, 1));

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(await waiting.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
			Assert.That(_api.FindDevice(_phone), Is.Not.Null);
		});
	}

	private async Task WaitForEventsAsync(int count)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (_events.Count < count && DateTime.UtcNow < deadline)
		{
			await Task.Delay(10);
		}
	}

	private static string Name(Guid id)
	{
		return id == _phone ? "phone" : "tablet";
	}

	private void Push(long revision, string access, params CompanionDeviceStateDto[] devices)
	{
		_cache.Apply(new ProtocolEnvelope
		{
			Type = MessageTypes.HostState,
			Id = Guid.NewGuid().ToString(),
			Payload = JsonSerializer.SerializeToElement(new HostStatePayload
				{
					Api = HostApis.CompanionDevices,
					Data = JsonSerializer.SerializeToElement(new CompanionDevicesStateDto
						{
							Access = access, Devices = devices, Revision = revision
						},
						PluginProtocolJson.Options)
				},
				PluginProtocolJson.Options)
		});
	}

	private static CompanionDeviceStateDto Device(Guid id, long readySequence, int? battery = null)
	{
		return new CompanionDeviceStateDto
			{ DeviceId = id.ToString("D"), ReadySequence = readySequence, BatteryLevelPercent = battery };
	}

	private static JsonElement Json(object value)
	{
		return JsonSerializer.SerializeToElement(value, PluginProtocolJson.Options);
	}

	private sealed class FailingHostInvoker(ProtocolError error) : IHostInvoker
	{
		public Task<JsonElement?> InvokeAsync(
			string api,
			string operation,
			object? arguments,
			CancellationToken cancellationToken)
		{
			return Task.FromException<JsonElement?>(HostInvocationException.From(error));
		}

		public bool TryComplete(ProtocolEnvelope result)
		{
			return false;
		}
	}

	private sealed class ScriptedHostAssets : IPluginHostAssetReceiver
	{
		public Dictionary<string, HostAssetTransfer> Transfers { get; } = new(StringComparer.Ordinal);

		public Task<HostAssetTransfer> AwaitAsync(string transferId, CancellationToken cancellationToken)
		{
			return Task.FromResult(Transfers[transferId]);
		}

		public ProtocolEnvelope Handle(ProtocolEnvelope envelope)
		{
			throw new NotSupportedException();
		}

		public void Reset()
		{
		}
	}
}
