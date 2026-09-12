using System.Security.Claims;
using System.Text.Json;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Devices;
using MacroDeckHost.Integrations;
using MacroDeckHost.Integrations.Companion.Actions;
using MacroDeckHost.Ui;

namespace MacroDeckHost.Tests.UnitTests.Companion;

[TestFixture]
internal sealed class CompanionStateAndActionsTests
{
	[Test]
	public async Task Closing_the_last_connection_reports_disconnected_and_makes_values_unavailable()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		await harness.ReportAsync("connection-1", device);
		await harness.ReportAsync("connection-2", device);

		harness.DeviceRegistry.Disconnected("connection-1");
		var connectedWhileOneRemains = await harness.ReadAsync(device, "is_connected");
		var batteryWhileOneRemains = await harness.ReadAsync(device, "battery_level_percent");
		harness.DeviceRegistry.Disconnected("connection-2");

		Assert.Multiple(async () =>
		{
			Assert.That(connectedWhileOneRemains.Value, Is.EqualTo(true));
			Assert.That(batteryWhileOneRemains.Value, Is.EqualTo(80));
			Assert.That((await harness.ReadAsync(device, "is_connected")).Value, Is.EqualTo(false));
			foreach (var slot in new[] { "battery_level_percent", "charging", "orientation", "app_version" })
			{
				Assert.That(await harness.ReadAsync(device, slot), Is.SameAs(VariableReading.Unavailable), slot);
			}
		});
	}

	[Test]
	public async Task A_report_carrying_a_new_app_version_updates_app_version()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		await harness.ReportAsync("connection-1", device, CompanionHarness.Report(appVersion: "1.0.0"));

		await harness.ReportAsync("connection-1", device, CompanionHarness.Report(appVersion: "1.1.0"));

		Assert.That((await harness.ReadAsync(device, "app_version")).Value, Is.EqualTo("1.1.0"));
	}

	[Test]
	public async Task An_action_against_an_offline_device_fails_as_not_connected()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		await harness.ReportAsync("connection-1", device);
		harness.DeviceRegistry.Disconnected("connection-1");

		var result = await ExecuteAsync(harness, "vibrate", device);

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConnected));
			Assert.That(harness.Transport.GroupMessages, Is.Empty);
		});
	}

	[Test]
	public async Task An_action_against_an_online_device_sends_exactly_one_command_to_that_device()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		var other = harness.AddDevice("Tablet");
		await harness.ReportAsync("connection-1", device);
		await harness.ReportAsync("connection-2", other);

		var result = await ExecuteAsync(harness,
			"set-brightness",
			device,
			(CompanionActions.BrightnessParameter, "40"));

		var (group, message) = harness.Transport.GroupMessages.Single();
		var command = (CompanionCommandEvent)message;
		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(group, Is.EqualTo(UiDeviceGroups.For(device)));
			Assert.That(command.Command, Is.EqualTo("setBrightness"));
			Assert.That(command.BrightnessPercent, Is.EqualTo(40));
			Assert.That(command.Orientation, Is.Null);
		});
	}

	[TestCase("set-brightness", CompanionActions.BrightnessParameter, "bright")]
	[TestCase("set-brightness", CompanionActions.BrightnessParameter, "101")]
	[TestCase("set-orientation", CompanionActions.OrientationParameter, "sideways")]
	public async Task An_invalid_parameter_fails_as_invalid_parameter(string actionId, string parameter, string value)
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		await harness.ReportAsync("connection-1", device);

		var result = await ExecuteAsync(harness, actionId, device, (parameter, value));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(harness.Transport.GroupMessages, Is.Empty);
		});
	}

	[Test]
	public void A_report_from_a_token_without_a_device_claim_is_refused()
	{
		var harness = new CompanionHarness();
		using var dispatcher = Dispatcher(harness, new ClaimsPrincipal(new ClaimsIdentity([], "test")));

		var refusal = Assert.ThrowsAsync<UiWebSocketDispatchException>(async () =>
			await dispatcher.DispatchAsync("ReportCompanionState",
				Payload(new { batteryLevelPercent = 50 }),
				CancellationToken.None));

		Assert.That(refusal!.Code, Is.EqualTo("forbidden"));
	}

	[Test]
	public async Task A_report_is_clamped_and_sanitized_before_it_is_kept()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		using var dispatcher = Dispatcher(harness,
			new ClaimsPrincipal(new ClaimsIdentity([new Claim(AuthDefaults.DeviceClaim, device.ToString())], "test")));

		var response = await dispatcher.DispatchAsync("ReportCompanionState",
			Payload(new
			{
				batteryLevelPercent = 150,
				charging = true,
				orientation = "sideways",
				screenBrightnessPercent = -5,
				model = new string('m', 100)
			}),
			CancellationToken.None);
		await harness.DeviceRegistry.CreationFor(device).WaitAsync(TimeSpan.FromSeconds(5));

		harness.DeviceRegistry.TryGetState(device, out var state);
		Assert.Multiple(() =>
		{
			Assert.That(response, Is.Null);
			Assert.That(state.BatteryLevelPercent, Is.EqualTo(100));
			Assert.That(state.ScreenBrightnessPercent, Is.Zero);
			Assert.That(state.Orientation, Is.Null);
			Assert.That(state.Model, Has.Length.EqualTo(64));
			Assert.That(state.Charging, Is.True);
		});
	}

	private static async Task<ActionResult> ExecuteAsync(CompanionHarness harness,
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

		var action = harness.Integration.Actions.Single(candidate => candidate.Id == actionId);
		return await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext { Parameters = values });
	}

	private static JsonElement Payload(object value) =>
		JsonSerializer.SerializeToElement(value, UiWebSocketProtocol.Json);

	private static UiWebSocketDispatcher Dispatcher(CompanionHarness harness, ClaimsPrincipal principal)
		=> new(connectionId: "connection-1",
			principal: principal,
			abort: static () => { },
			labelText: null!,
			subscriptions: null!,
			widgetState: null!,
			widgetStateSubscriptions: null!,
			variableInterest: null!,
			variableBroadcaster: null!,
			getMusicPlayerInstances: null!,
			getMusicPlayerState: null!,
			getWeatherInstances: null!,
			getFolderViews: null!,
			getWeatherState: null!,
			getVariableCatalogProviders: null!,
			discoverCatalogVariables: null!,
			resolveCatalogVariable: null!,
			reportFolderChanged: null!,
			listUiPreviews: null!,
			logSubscriptions: null!,
			logFileReader: null!,
			deviceConnections: null!,
			musicPlayerClientSync: null!,
			uiSessions: null!,
			configUiSessions: null!,
			widgetUiSessions: null!,
			uiPreviewSessions: null!,
			folderUiSessions: null!,
			modalUiSessions: null!,
			lifetime: null!,
			transport: null!,
			webSocketTransport: null!,
			companions: harness.DeviceRegistry,
			connectionCancellation: CancellationToken.None);
}
