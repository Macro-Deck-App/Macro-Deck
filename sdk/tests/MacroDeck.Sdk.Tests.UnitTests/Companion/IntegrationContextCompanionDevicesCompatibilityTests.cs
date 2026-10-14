using MacroDeck.Sdk.Companion;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;

namespace MacroDeck.Sdk.Tests.UnitTests.Companion;

[TestFixture]
public class IntegrationContextCompanionDevicesCompatibilityTests
{
	private static readonly Guid _deviceId = Guid.NewGuid();

	[Test]
	public async Task A_context_written_before_companion_devices_existed_still_builds_and_offers_none()
	{
		IIntegrationContext context = new ContextWrittenBeforeCompanionDevices();
		var devices = context.CompanionDevices;
		var raised = false;
		devices.AccessChanged += (_, _) => raised = true;
		devices.DeviceReady += (_, _) => raised = true;
		devices.DeviceChanged += (_, _) => raised = true;

		var waited = await devices.WaitForDeviceAsync(_deviceId);
		var sent = await devices.SendAsync(_deviceId, CompanionDeviceCommand.Vibrate);
		var requested = await devices.RequestAsync(_deviceId, CompanionDeviceCommand.ScreenOn);
		var screenshot = await devices.CaptureScreenshotAsync(_deviceId, CompanionScreenshotMode.Deck);

		Assert.Multiple(() =>
		{
			Assert.That(devices.Access, Is.EqualTo(CompanionDeviceAccess.Unsupported));
			Assert.That(devices.Devices, Is.Empty);
			Assert.That(devices.FindDevice(_deviceId), Is.Null);
			Assert.That(waited, Is.False);
			Assert.That(sent, Is.False);
			Assert.That(requested,
				Is.EqualTo(CompanionDeviceCommandResult.Failed(CompanionDeviceFailure.NotConnected)));
			Assert.That(screenshot.Png, Is.Null);
			Assert.That(screenshot.Failure, Is.EqualTo(CompanionDeviceFailure.NotConnected));
			Assert.That(raised, Is.False);
		});
	}

	[TestCase(-1)]
	[TestCase(101)]
	public void A_brightness_outside_zero_to_one_hundred_is_refused(int percent)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => CompanionDeviceCommand.SetBrightness(percent));
	}

	[Test]
	public void An_orientation_the_devices_do_not_know_is_refused()
	{
		Assert.Throws<ArgumentException>(() => CompanionDeviceCommand.SetOrientation("upside-down"));
	}

	[Test]
	public void A_command_carries_only_the_value_its_kind_uses()
	{
		var brightness = CompanionDeviceCommand.SetBrightness(30);
		var orientation = CompanionDeviceCommand.SetOrientation(CompanionOrientations.Automatic);

		Assert.Multiple(() =>
		{
			Assert.That((brightness.Kind, brightness.BrightnessPercent, brightness.Orientation),
				Is.EqualTo((CompanionDeviceCommandKind.SetBrightness, (int?)30, (string?)null)));
			Assert.That((orientation.Kind, orientation.BrightnessPercent, orientation.Orientation),
				Is.EqualTo((CompanionDeviceCommandKind.SetOrientation, (int?)null, (string?)"automatic")));
		});
	}

	[Test]
	public void A_screenshot_result_has_either_a_png_or_a_failure()
	{
		var png = CompanionScreenshotResult.Of([0x89, 0x50]);
		var failed = CompanionScreenshotResult.Failed(CompanionDeviceFailure.ConsentDenied);

		Assert.Multiple(() =>
		{
			Assert.That(png.Png, Is.EqualTo(new byte[] { 0x89, 0x50 }));
			Assert.That(png.Failure, Is.Null);
			Assert.That(failed.Png, Is.Null);
			Assert.That(failed.Failure, Is.EqualTo(CompanionDeviceFailure.ConsentDenied));
		});
	}

	private sealed class ContextWrittenBeforeCompanionDevices : IIntegrationContext
	{
		public IVariableApi Variables => throw new NotSupportedException();

		public IUserVariableApi UserVariables => throw new NotSupportedException();

		public IIntegrationConfig Config => throw new NotSupportedException();

		public IDeckNavigator Deck => throw new NotSupportedException();

		public IScriptApi Scripts => throw new NotSupportedException();

		public IWidgetApi Widgets => throw new NotSupportedException();

		public IEventPublisher Events => throw new NotSupportedException();

		public IUserNotifier Notifications => throw new NotSupportedException();
	}
}
