using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Companion;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

[TestFixture]
public class A39CompanionDeviceFakeTests
{
	private static readonly Guid _phone = Guid.NewGuid();

	[Test]
	public void Connecting_a_device_again_is_a_new_connection_and_raises_ready_again()
	{
		var devices = new FakeCompanionDeviceApi();
		var ready = new List<long>();
		devices.DeviceReady += (_, e) => ready.Add(e.Device!.ReadySequence);

		devices.Connect(_phone);
		devices.Report(_phone, new CompanionDeviceState { BatteryLevelPercent = 40 });
		devices.Disconnect(_phone);
		devices.Connect(_phone);

		Assert.Multiple(() =>
		{
			Assert.That(ready, Has.Count.EqualTo(2));
			Assert.That(ready[1], Is.GreaterThan(ready[0]));
			Assert.That(devices.FindDevice(_phone)!.State.BatteryLevelPercent, Is.Null);
		});
	}

	[Test]
	public async Task A_request_ends_as_the_test_decides_and_is_recorded()
	{
		var devices = new FakeCompanionDeviceApi
		{
			OnRequest = (_, _) => CompanionDeviceCommandResult.Failed(CompanionDeviceFailure.ConsentDenied)
		};
		devices.Connect(_phone);

		var result = await devices.RequestAsync(_phone, CompanionDeviceCommand.ScreenOff);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(CompanionDeviceCommandResult.Failed(CompanionDeviceFailure.ConsentDenied)));
			Assert.That(devices.Calls.Single(),
				Is.EqualTo(new FakeCompanionDeviceCall(_phone,
					FakeCompanionDeviceCallKind.Request,
					CompanionDeviceCommand.ScreenOff,
					null)));
		});
	}

	[Test]
	public async Task A_plugin_that_is_not_allowed_sees_no_devices_and_is_refused()
	{
		var devices = new FakeCompanionDeviceApi();
		devices.Connect(_phone);
		devices.SetAccess(CompanionDeviceAccess.NotAllowed);

		var screenshot = await devices.CaptureScreenshotAsync(_phone, CompanionScreenshotMode.Deck);

		Assert.Multiple(() =>
		{
			Assert.That(devices.Devices, Is.Empty);
			Assert.That(screenshot.Failure, Is.EqualTo(CompanionDeviceFailure.NotAllowed));
		});
	}

	[Test]
	public async Task An_integration_under_the_harness_sees_no_companion_devices_until_the_test_grants_access()
	{
		var integration = new RecordingIntegration();
		await using var harness = PluginTestHarness.Create(builder => builder.RegisterIntegration(_ => integration));
		await harness.InitializeIntegrationsAsync();
		var before = integration.Devices!.Access;

		harness.Context.CompanionDevices.SetAccess(CompanionDeviceAccess.Available);
		harness.Context.CompanionDevices.Connect(_phone);

		Assert.Multiple(() =>
		{
			Assert.That(before, Is.EqualTo(CompanionDeviceAccess.Unsupported));
			Assert.That(integration.Devices.Devices.Select(device => device.Id), Is.EqualTo(new[] { _phone }));
		});
	}

	private sealed class RecordingIntegration : IPluginIntegration
	{
		public ICompanionDeviceApi? Devices { get; private set; }

		public IReadOnlyList<IActionDefinition> Actions => [];

		public Task InitializeAsync(IIntegrationContext context)
		{
			Devices = context.CompanionDevices;
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}
	}
}
