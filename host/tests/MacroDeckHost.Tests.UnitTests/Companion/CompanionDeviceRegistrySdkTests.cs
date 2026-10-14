using MacroDeck.Sdk.Companion;
using MacroDeckHost.Application.Ui.Transport.Messages.Devices;
using MacroDeckHost.Integrations;

namespace MacroDeckHost.Tests.UnitTests.Companion;

[TestFixture]
internal sealed class CompanionDeviceRegistrySdkTests
{
	private static readonly byte[] _png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 7];

	[Test]
	public async Task A_reporting_device_is_listed_ready_once_per_connection_and_gone_after_it_disconnects()
	{
		var harness = new CompanionHarness();
		var devices = harness.DeviceRegistry;
		var device = harness.AddDevice("Phone");
		var events = new List<string>();
		devices.DeviceReady += (_, e) => events.Add($"ready:{e.Device!.ReadySequence}");
		devices.DeviceChanged += (_, e)
			=> events.Add(e.Device is null ? "gone" : $"changed:{e.Device.State.BatteryLevelPercent}");

		await harness.ReportAsync("connection-1", device);
		var first = devices.FindDevice(device)!;
		await harness.ReportAsync("connection-1", device, WithBattery(70));
		harness.DeviceRegistry.Disconnected("connection-1");
		var afterDisconnect = devices.Devices.Count;
		await harness.ReportAsync("connection-2", device);
		var second = devices.FindDevice(device)!;

		Assert.Multiple(() =>
		{
			Assert.That(devices.Access, Is.EqualTo(CompanionDeviceAccess.Available));
			Assert.That(first.State.Model, Is.EqualTo("Pixel 8"));
			Assert.That(afterDisconnect, Is.Zero);
			Assert.That(second.ReadySequence, Is.Not.EqualTo(first.ReadySequence));
			Assert.That(events,
				Is.EqualTo(new[]
				{
					"changed:80", $"ready:{first.ReadySequence}", "changed:70", "gone", "changed:80",
					$"ready:{second.ReadySequence}"
				}));
		});
	}

	[Test]
	public async Task A_handler_that_throws_does_not_keep_the_others_from_hearing_about_a_device()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		var heard = false;
		harness.DeviceRegistry.DeviceChanged += (_, _) => throw new InvalidOperationException("broken plugin handler");
		harness.DeviceRegistry.DeviceChanged += (_, _) => heard = true;

		await harness.ReportAsync("connection-1", device);

		Assert.That(heard, Is.True);
	}

	[Test]
	public async Task A_request_the_device_confirms_is_done_and_one_it_only_claims_is_unconfirmed()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		await harness.ReportAsync("connection-1", device, Confirming());

		var confirmed = harness.DeviceRegistry.RequestAsync(device, CompanionDeviceCommand.SetBrightness(20));
		var firstId = LastRequestId(harness);
		harness.Requests.TryComplete(device, firstId, CompanionCommandResult.Done, CompanionRequestKind.Command, false);
		var claimed = harness.DeviceRegistry.RequestAsync(device, CompanionDeviceCommand.Vibrate);
		var secondId = LastRequestId(harness);
		harness.Requests.TryClaim(device, secondId, CompanionRequestKind.Command);
		harness.Time.Advance(CompanionCommandRequests.ClaimedCommandDeadline + TimeSpan.FromSeconds(1));

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(await confirmed.WaitAsync(TimeSpan.FromSeconds(5)),
				Is.EqualTo(CompanionDeviceCommandResult.Done));
			Assert.That(await claimed.WaitAsync(TimeSpan.FromSeconds(5)),
				Is.EqualTo(CompanionDeviceCommandResult.Unconfirmed));
			Assert.That(LastCommand(harness).BrightnessPercent, Is.Null);
			Assert.That(harness.Transport.GroupMessages.Select(entry => entry.Message)
					.OfType<CompanionCommandEvent>()
					.Select(message => (message.Command, message.BrightnessPercent))
					.First(),
				Is.EqualTo(("setBrightness", (int?)20)));
		});
	}

	[Test]
	public async Task A_screenshot_arrives_as_png_and_a_declined_one_as_consent_denied()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		await harness.ReportAsync("connection-1", device, Confirming());

		var taken = harness.DeviceRegistry.CaptureScreenshotAsync(device, CompanionScreenshotMode.FullScreen);
		var takenCommand = LastCommand(harness);
		harness.Requests.TryComplete(device,
			takenCommand.RequestId!,
			CompanionCommandResult.Of(_png),
			CompanionRequestKind.Screenshot,
			false);
		var declined = harness.DeviceRegistry.CaptureScreenshotAsync(device, CompanionScreenshotMode.FullScreen);
		harness.Requests.TryComplete(device,
			LastRequestId(harness),
			CompanionCommandResult.Failed(CompanionDeviceFailure.ConsentDenied),
			CompanionRequestKind.Screenshot,
			false);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That((takenCommand.Command, takenCommand.ScreenshotMode), Is.EqualTo(("screenshot", "full")));
			Assert.That((await taken.WaitAsync(TimeSpan.FromSeconds(5))).Png, Is.EqualTo(_png));
			Assert.That((await declined.WaitAsync(TimeSpan.FromSeconds(5))).Failure,
				Is.EqualTo(CompanionDeviceFailure.ConsentDenied));
		});
	}

	[Test]
	public async Task A_command_for_a_device_that_is_not_connected_is_not_sent()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");

		var sent = await harness.DeviceRegistry.SendAsync(device, CompanionDeviceCommand.Vibrate);

		Assert.Multiple(() =>
		{
			Assert.That(sent, Is.False);
			Assert.That(harness.Transport.GroupMessages, Is.Empty);
		});
	}

	private static ReportCompanionStateRequest Confirming()
	{
		var report = CompanionHarness.Report();
		report.RequestableCapabilities = [];
		return report;
	}

	private static ReportCompanionStateRequest WithBattery(int percent)
	{
		var report = CompanionHarness.Report();
		report.BatteryLevelPercent = percent;
		return report;
	}

	private static CompanionCommandEvent LastCommand(CompanionHarness harness)
	{
		return harness.Transport.GroupMessages.Select(entry => entry.Message).OfType<CompanionCommandEvent>().Last();
	}

	private static string LastRequestId(CompanionHarness harness)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (DateTime.UtcNow < deadline && LastCommand(harness).RequestId is null)
		{
			Thread.Sleep(5);
		}

		return LastCommand(harness).RequestId!;
	}
}
