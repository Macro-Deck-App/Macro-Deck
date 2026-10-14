using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Companion;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Infrastructure;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Integrations.Companion;
using MacroDeckHost.Integrations.Companion.Actions;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Companion;

[TestFixture]
internal sealed class CompanionIntegrationSdkTests
{
	[Test]
	public async Task An_entry_reports_disconnected_before_the_first_initialization_and_follows_the_device_after_it()
	{
		var (harness, device) = await WithEntryAsync();
		var devices = new ScriptedCompanionDevices();
		devices.Connect(device, new CompanionDeviceState());
		var integration = new CompanionIntegration();
		var entry = Entry(harness);

		var beforeInit = await integration.GetEntryStatusAsync(entry);
		await integration.InitializeAsync(Context(harness, devices));
		var connected = await integration.GetEntryStatusAsync(entry);
		devices.Disconnect(device);
		var gone = await integration.GetEntryStatusAsync(entry);
		await integration.ShutdownAsync();

		Assert.Multiple(() =>
		{
			Assert.That(beforeInit, Is.EqualTo(ConfigEntryStatus.Disconnected));
			Assert.That(connected, Is.EqualTo(ConfigEntryStatus.Connected));
			Assert.That(gone, Is.EqualTo(ConfigEntryStatus.Disconnected));
			Assert.That(devices.SubscriberCount, Is.Zero);
		});
	}

	[Test]
	public async Task A_device_is_announced_once_per_connection_with_its_variables_read_from_the_sdk_state()
	{
		var (harness, device) = await WithEntryAsync();
		var devices = new ScriptedCompanionDevices();
		var integration = new CompanionIntegration();
		await integration.InitializeAsync(Context(harness, devices));
		var before = harness.Events.Published.Count;

		devices.Connect(device, new CompanionDeviceState { BatteryLevelPercent = 33, IsCharging = true });
		devices.Report(device, new CompanionDeviceState { BatteryLevelPercent = 32, IsCharging = true });
		devices.Disconnect(device);
		devices.Connect(device, new CompanionDeviceState { BatteryLevelPercent = 31 });
		var battery = await integration.ReadAsync($"entry-{device:N}-battery-level-percent");
		var charging = await integration.ReadAsync($"entry-{device:N}-charging");

		Assert.Multiple(() =>
		{
			Assert.That(harness.Events.Published.Count - before, Is.EqualTo(2));
			Assert.That(battery.Value, Is.EqualTo(31));
			Assert.That(charging.Value, Is.EqualTo(false));
		});
	}

	[Test]
	public async Task A_confirming_device_gets_a_request_and_an_unconfirmed_answer_still_succeeds()
	{
		var (harness, device) = await WithEntryAsync();
		var devices = new ScriptedCompanionDevices { RequestResult = CompanionDeviceCommandResult.Unconfirmed };
		devices.Connect(device,
			new CompanionDeviceState
			{
				Capabilities = new HashSet<string> { CompanionDeviceCapabilities.ScreenOn },
				ConfirmsCommands = true
			});
		var integration = new CompanionIntegration();
		await integration.InitializeAsync(Context(harness, devices));

		var result = await ExecuteAsync(integration, "screen-on", device);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(devices.Calls.Single(),
				Is.EqualTo(("request", device, (CompanionDeviceCommand?)CompanionDeviceCommand.ScreenOn)));
		});
	}

	[Test]
	public async Task A_screenshot_the_user_declines_on_the_device_is_a_permission_failure()
	{
		var (harness, device) = await WithEntryAsync();
		var devices = new ScriptedCompanionDevices
		{
			ScreenshotResult = CompanionScreenshotResult.Failed(CompanionDeviceFailure.ConsentDenied)
		};
		devices.Connect(device,
			new CompanionDeviceState
			{
				RequestableCapabilities = new HashSet<string> { CompanionDeviceCapabilities.ScreenshotFull },
				ConfirmsCommands = true
			});
		var integration = new CompanionIntegration();
		await integration.InitializeAsync(Context(harness, devices));

		var result = await ExecuteAsync(integration,
			"take-screenshot",
			device,
			(TakeScreenshotAction.ModeParameter, TakeScreenshotAction.FullMode),
			(TakeScreenshotAction.FolderParameter, Path.Combine(Path.GetTempPath(), $"companion-{Guid.NewGuid():N}")));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.PermissionDenied));
			Assert.That(devices.Calls.Single().Operation, Is.EqualTo("screenshot:FullScreen"));
		});
	}

	private static async Task<(CompanionHarness Harness, Guid Device)> WithEntryAsync()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		await harness.ReportAsync("connection-1", device);
		harness.DeviceRegistry.Disconnected("connection-1");
		await harness.Integration.ShutdownAsync();
		return (harness, device);
	}

	private static StoredConfigEntry Entry(CompanionHarness harness)
	{
		var record = harness.Entries.Single();
		return new StoredConfigEntry { Id = record.Id, Title = record.Title, Values = record.Values };
	}

	private static IntegrationContext Context(CompanionHarness harness, ICompanionDeviceApi devices)
	{
		return new IntegrationContext(harness.VariableApi,
			harness.UserVariables,
			new IntegrationConfig(CompanionHarness.IntegrationId, harness.ScopeFactory),
			null!,
			null!,
			null!,
			harness.Events,
			null!,
			null!,
			null!,
			UnavailableNotificationService.Instance,
			companionDevices: devices);
	}

	private static Task<ActionResult> ExecuteAsync(
		CompanionIntegration integration,
		string actionId,
		Guid device,
		params (string Name, object Value)[] parameters)
	{
		var values = new Dictionary<string, object>
			{ [CompanionTargetResolver.ConfigurationParameter] = device.ToString("D") };
		foreach (var (name, value) in parameters)
		{
			values[name] = value;
		}

		return integration.Actions.Single(action => action.Id == actionId)
			.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext { Parameters = values });
	}
}
