using System.Text.Json;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Integrations;
using Serilog;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.Companion;

[TestFixture]
internal sealed class CompanionDeviceRemovalTests
{
	private const string IntegrationId = CompanionHarness.IntegrationId;

	[Test]
	public async Task Removing_the_last_device_deletes_its_entry_and_variables_and_a_new_device_gets_an_entry_again()
	{
		var harness = new CompanionHarness();
		var removed = harness.AddDevice("Old phone");
		await harness.ReportAsync("connection-1", removed);
		var variablesBefore = await harness.Variables.GetByOwnerIntegration(IntegrationId);

		harness.RemoveDeviceRow(removed);
		await Handler(harness).Handle(new DeviceRemovedNotification(removed), CancellationToken.None);
		var variablesAfter = await harness.Variables.GetByOwnerIntegration(IntegrationId);
		var explicitlyDisabledAfterRemoval = harness.Registry.IsExplicitlyDisabled(IntegrationId);
		var storedChoiceAfterRemoval = harness.StateStore.States.ContainsKey(IntegrationId);

		var replacement = harness.AddDevice("New phone");
		await harness.ReportAsync("connection-2", replacement);

		Assert.Multiple(() =>
		{
			Assert.That(variablesBefore, Has.Count.EqualTo(8));
			Assert.That(variablesAfter, Is.Empty);
			Assert.That(explicitlyDisabledAfterRemoval, Is.False);
			Assert.That(storedChoiceAfterRemoval, Is.False);
			Assert.That(harness.Entries.Select(entry => entry.Id), Is.EqualTo(new[] { replacement }));
			Assert.That(harness.DeviceRegistry.TryGetState(removed, out _), Is.False);
		});
	}

	[Test]
	public async Task Removing_a_device_keeps_an_integration_the_user_switched_off_disabled()
	{
		var harness = new CompanionHarness();
		var removed = harness.AddDevice("Old phone");
		await harness.ReportAsync("connection-1", removed);
		harness.Registry.SetEnabled(IntegrationId, false);

		harness.RemoveDeviceRow(removed);
		await Handler(harness).Handle(new DeviceRemovedNotification(removed), CancellationToken.None);
		var other = harness.AddDevice("Tablet");
		await harness.ReportAsync("connection-2", other);

		Assert.Multiple(() =>
		{
			Assert.That(harness.Entries, Is.Empty);
			Assert.That(harness.Registry.IsExplicitlyDisabled(IntegrationId), Is.True);
		});
	}

	[Test]
	public async Task An_entry_created_between_the_delete_and_the_clear_keeps_the_integration_enabled()
	{
		var harness = new CompanionHarness();
		var removed = harness.AddDevice("Old phone");
		await harness.ReportAsync("connection-1", removed);
		var other = harness.AddDevice("Tablet");
		harness.Coordinator.AfterDelete = async () =>
		{
			harness.Coordinator.AfterDelete = null;
			await harness.Coordinator.Inner.CompleteAsync(IntegrationId,
				other,
				"Tablet",
				new Dictionary<string, JsonElement>(),
				CancellationToken.None);
		};

		await harness.DeviceRegistry.RemoveDeviceAsync(removed, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(harness.Entries.Select(entry => entry.Id), Is.EqualTo(new[] { other }));
			Assert.That(harness.Registry.IsEnabled(IntegrationId), Is.True);
			Assert.That(harness.Registry.IsExplicitlyDisabled(IntegrationId), Is.False);
		});
	}

	[Test]
	public async Task A_removal_while_the_creation_is_blocked_leaves_no_entry()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		harness.Coordinator.BeforeComplete = () =>
		{
			entered.TrySetResult();
			return release.Task;
		};

		harness.DeviceRegistry.Report("connection-1", device, CompanionHarness.Report());
		var creation = harness.DeviceRegistry.CreationFor(device);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		harness.RemoveDeviceRow(device);
		var removal = harness.DeviceRegistry.RemoveDeviceAsync(device, CancellationToken.None);
		var removalFinishedWhileBlocked = removal.IsCompleted;

		release.SetResult();
		await creation.WaitAsync(TimeSpan.FromSeconds(5));
		await removal.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(removalFinishedWhileBlocked, Is.False);
			Assert.That(harness.Entries, Is.Empty);
		});
	}

	[Test]
	public async Task A_device_connecting_while_the_last_device_is_removed_gets_its_entry_right_away()
	{
		var harness = new CompanionHarness();
		var removed = harness.AddDevice("Old phone");
		await harness.ReportAsync("connection-1", removed);
		var late = harness.AddDevice("Tablet");
		harness.Coordinator.AfterDelete = async () =>
		{
			harness.Coordinator.AfterDelete = null;
			await harness.ReportAsync("connection-2", late);
		};

		await harness.DeviceRegistry.RemoveDeviceAsync(removed, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(harness.Entries.Select(entry => entry.Id), Is.EqualTo(new[] { late }));
			Assert.That(harness.Registry.IsExplicitlyDisabled(CompanionHarness.IntegrationId), Is.False);
		});
	}

	[Test]
	public async Task The_removal_handler_logs_and_swallows_a_coordinator_failure()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		await harness.ReportAsync("connection-1", device);
		harness.Coordinator.DeleteFailure = new InvalidOperationException("coordinator failed");
		var sink = new CompanionHarness.CapturingSink();
		var handler = new CompanionDeviceRemovedHandler(harness.DeviceRegistry,
			new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger());

		Assert.DoesNotThrowAsync(async () =>
			await handler.Handle(new DeviceRemovedNotification(device), CancellationToken.None));
		Assert.That(sink.Events.Any(e => e.Level == LogEventLevel.Error && e.Exception is InvalidOperationException),
			Is.True);
	}

	private static CompanionDeviceRemovedHandler Handler(CompanionHarness harness)
		=> new(harness.DeviceRegistry, CompanionHarness.Logger);
}
