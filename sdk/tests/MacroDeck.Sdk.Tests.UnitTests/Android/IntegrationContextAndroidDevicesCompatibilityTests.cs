using MacroDeck.Sdk.Android;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;

namespace MacroDeck.Sdk.Tests.UnitTests.Android;

[TestFixture]
public class IntegrationContextAndroidDevicesCompatibilityTests
{
	[Test]
	public void A_context_written_before_android_devices_existed_offers_no_adb_and_never_raises()
	{
		IIntegrationContext context = new ContextWrittenBeforeAndroidDevices();
		var raised = false;
		var devices = context.AndroidDevices;

		devices.AccessChanged += (_, _) => raised = true;
		devices.DeviceConnected += (_, _) => raised = true;
		devices.DeviceDisconnected += (_, _) => raised = true;
		devices.DeviceStateChanged += (_, _) => raised = true;
		var connect = Assert.ThrowsAsync<AndroidDeviceException>(() => devices.ConnectAsync("192.168.1.20:5555"));

		Assert.Multiple(() =>
		{
			Assert.That(devices.Access, Is.EqualTo(AndroidDeviceAccess.Unsupported));
			Assert.That(devices.Devices, Is.Empty);
			Assert.That(devices.FindDevice("R5CT20ABCDE"), Is.Null);
			Assert.That(connect!.ErrorCode, Is.EqualTo(AndroidDeviceErrorCode.Unsupported));
			Assert.That(raised, Is.False);
		});
	}

	[Test]
	public void Device_info_built_the_old_way_has_no_stable_id_and_the_stable_id_takes_part_in_equality()
	{
		var old = new AndroidDeviceInfo("Pixel 8", "Google", "shiba");

		Assert.Multiple(() =>
		{
			Assert.That(old.StableId, Is.Null);
			Assert.That(old, Is.EqualTo(new AndroidDeviceInfo("Pixel 8", "Google", "shiba")));
			Assert.That(old with { StableId = "3fa9c2d1e0b4a756" }, Is.Not.EqualTo(old));
		});
	}

	private sealed class ContextWrittenBeforeAndroidDevices : IIntegrationContext
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
