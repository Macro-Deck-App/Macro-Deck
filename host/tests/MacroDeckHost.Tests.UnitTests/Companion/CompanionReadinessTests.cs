using System.Text.Json;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Identity;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Ui.Transport.Messages.Devices;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Integrations;
using MacroDeckHost.Integrations.Companion;
using MacroDeckHost.Integrations.Companion.Actions;

namespace MacroDeckHost.Tests.UnitTests.Companion;

[TestFixture]
internal sealed class CompanionReadinessTests
{
	private const string DeviceReadyId = "device-ready";

	private static readonly TimeSpan _settle = TimeSpan.FromSeconds(5);

	[Test]
	public async Task A_Companion_action_started_before_a_reconnected_device_reports_runs_once_it_reports()
	{
		var (harness, device) = await PairedDeviceAsync();
		Connect(harness, "connection-2", device);

		var run = CompanionStateAndActionsTests.ExecuteAsync(harness, "screen-on", device);
		var pendingBeforeReport = !run.IsCompleted;
		await harness.ReportAsync("connection-2", device, CompanionHarness.Report(capabilities: "screenOn"));
		var result = await run.WaitAsync(_settle);

		Assert.Multiple(() =>
		{
			Assert.That(pendingBeforeReport, Is.True);
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(SentCommands(harness), Is.EqualTo(new[] { CompanionCommand.ScreenOn }));
		});
	}

	[Test]
	public async Task A_Companion_action_for_a_device_without_a_connection_fails_without_waiting()
	{
		var (harness, device) = await PairedDeviceAsync();

		var result = await CompanionStateAndActionsTests.ExecuteAsync(harness, "screen-on", device).WaitAsync(_settle);

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConnected));
	}

	[Test]
	public async Task A_Companion_action_for_a_connected_device_that_never_reports_fails_after_the_timeout()
	{
		var (harness, device) = await PairedDeviceAsync();
		Connect(harness, "connection-2", device);
		var timers = harness.Time.ActiveTimerCount;

		var run = CompanionStateAndActionsTests.ExecuteAsync(harness, "screen-on", device);
		await WaitUntilAsync(() => harness.Time.ActiveTimerCount > timers);
		var pendingBeforeTimeout = !run.IsCompleted;
		harness.Time.Advance(CompanionDeviceRegistry.StateReportTimeout + TimeSpan.FromSeconds(1));
		var result = await run.WaitAsync(_settle);

		Assert.Multiple(() =>
		{
			Assert.That(pendingBeforeTimeout, Is.True);
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConnected));
			Assert.That(SentCommands(harness), Is.Empty);
		});
	}

	[Test]
	public async Task A_Companion_action_fails_when_the_connection_closes_before_the_device_reports()
	{
		var (harness, device) = await PairedDeviceAsync();
		Connect(harness, "connection-2", device);
		var timers = harness.Time.ActiveTimerCount;

		var run = CompanionStateAndActionsTests.ExecuteAsync(harness, "screen-on", device);
		await WaitUntilAsync(() => harness.Time.ActiveTimerCount > timers);
		Disconnect(harness, "connection-2");
		var result = await run.WaitAsync(_settle);

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConnected));
	}

	[Test]
	public async Task An_invalid_parameter_fails_without_waiting_for_a_connected_device_to_report()
	{
		var (harness, device) = await PairedDeviceAsync();
		Connect(harness, "connection-2", device);

		var result = await CompanionStateAndActionsTests
			.ExecuteAsync(harness, "set-brightness", device, (CompanionActions.BrightnessParameter, "bright"))
			.WaitAsync(_settle);

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
	}

	[Test]
	public async Task Device_ready_is_raised_once_each_time_a_paired_device_becomes_ready()
	{
		var (harness, device) = await PairedDeviceAsync();
		var afterPairing = ReadyEvents(harness).Count;

		await harness.ReportAsync("connection-1b", device);
		Connect(harness, "connection-2", device);
		await harness.ReportAsync("connection-2", device);
		await harness.ReportAsync("connection-2", device);
		var whileConnected = ReadyEvents(harness).Count;
		Disconnect(harness, "connection-2");
		Disconnect(harness, "connection-1b");
		Connect(harness, "connection-3", device);
		await harness.ReportAsync("connection-3", device);

		Assert.Multiple(() =>
		{
			Assert.That(afterPairing, Is.EqualTo(1));
			Assert.That(whileConnected, Is.EqualTo(2));
			Assert.That(ReadyEvents(harness), Has.Count.EqualTo(3));
			Assert.That(ReadyEvents(harness).Last()["deviceId"], Is.EqualTo(device.ToString("D")));
			Assert.That(ReadyEvents(harness).Last()["deviceName"], Is.EqualTo("Phone"));
		});
	}

	[Test]
	public async Task Reinitializing_the_integration_does_not_repeat_device_ready_for_a_connected_device()
	{
		var (harness, device) = await PairedDeviceAsync();
		Connect(harness, "connection-2", device);
		await harness.ReportAsync("connection-2", device);
		var before = ReadyEvents(harness).Count;

		await harness.Integration.ShutdownAsync();
		await harness.Integration.InitializeAsync(harness.Context);

		Assert.That(ReadyEvents(harness), Has.Count.EqualTo(before));
	}

	[Test]
	public async Task A_device_that_became_ready_while_the_integration_was_off_is_announced_when_it_starts()
	{
		var (harness, device) = await PairedDeviceAsync();
		await harness.Integration.ShutdownAsync();
		Connect(harness, "connection-2", device);
		await harness.ReportAsync("connection-2", device);
		var whileOff = ReadyEvents(harness).Count;

		await harness.Integration.InitializeAsync(harness.Context);

		Assert.Multiple(() =>
		{
			Assert.That(whileOff, Is.EqualTo(1));
			Assert.That(ReadyEvents(harness), Has.Count.EqualTo(2));
		});
	}

	[Test]
	public async Task A_device_ready_trigger_filtered_to_one_device_matches_only_that_device()
	{
		var (harness, device) = await PairedDeviceAsync();
		var other = Guid.NewGuid();
		var definition = harness.Integration.EventDefinitions.Single(candidate => candidate.Id == DeviceReadyId);
		var descriptor = new EventDefinitionDescriptor(QualifiedId.Create(CompanionHarness.IntegrationId, DeviceReadyId),
			CompanionHarness.IntegrationId,
			harness.Integration.Name,
			true,
			definition);
		var published = ReadyEvents(harness).Single();
		var matcher = new EventSubscriptionMatcher(new ActionConditionEvaluator(new VariableTemplateRenderer(new())));
		var context = await new VariableTemplateRenderer(new()).CreateContextAsync(VariableScope.Global, null);

		Assert.Multiple(() =>
		{
			Assert.That(matcher.Matches(FilteredTo(device), descriptor, context.WithEvent(published)), Is.True);
			Assert.That(matcher.Matches(FilteredTo(other), descriptor, context.WithEvent(published)), Is.False);
		});
	}

	[Test]
	public void The_device_ready_event_is_localized()
	{
		var definition = new CompanionIntegration().EventDefinitions.Single(candidate => candidate.Id == DeviceReadyId);

		Assert.Multiple(() =>
		{
			Assert.That(definition.Name.IsLocalized, Is.True);
			Assert.That(definition.Description.IsLocalized, Is.True);
			Assert.That(definition.Category.IsLocalized, Is.True);
			Assert.That(definition.ConfigurationParameters.Concat(definition.PayloadParameters)
				.All(parameter => parameter.Label.IsLocalized), Is.True);
		});
	}

	private static async Task<(CompanionHarness Harness, Guid Device)> PairedDeviceAsync()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone", DeviceClientType.Native);
		Connect(harness, "connection-1", device);
		await harness.ReportAsync("connection-1", device);
		Disconnect(harness, "connection-1");
		harness.Transport.GroupMessages.Clear();
		return (harness, device);
	}

	private static void Connect(CompanionHarness harness, string connectionId, Guid device)
	{
		harness.Connections.Attach(connectionId, device, () => { });
		harness.Connections.Register(connectionId, $"client-{connectionId}");
	}

	private static void Disconnect(CompanionHarness harness, string connectionId)
	{
		harness.Connections.Remove(connectionId);
		harness.DeviceRegistry.Disconnected(connectionId);
	}

	private static List<IReadOnlyDictionary<string, object?>> ReadyEvents(CompanionHarness harness)
		=> [.. harness.Events.Published.Where(entry => entry.EventId == DeviceReadyId).Select(entry => entry.Parameters!)];

	private static List<string> SentCommands(CompanionHarness harness)
		=> [.. harness.Transport.GroupMessages.Select(entry => entry.Message).OfType<CompanionCommandEvent>()
			.Select(message => message.Command)];

	private static EventSubscription FilteredTo(Guid device)
		=> new(EventTriggerOwner.ForAutomation(Guid.NewGuid()),
			"t1",
			$"{CompanionHarness.IntegrationId}::{DeviceReadyId}",
			new Dictionary<string, EventConfigurationValue>(StringComparer.Ordinal)
			{
				["deviceId"] = new(JsonSerializer.SerializeToElement(device.ToString("D")), null)
			},
			null);

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + _settle;
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail("The condition was not met in time.");
			}

			await Task.Delay(10);
		}
	}
}
