using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Companion;

[TestFixture]
internal sealed class CompanionUserChoiceTests
{
	private const string IntegrationId = CompanionHarness.IntegrationId;

	[Test]
	public async Task A_user_switching_off_before_the_configuration_is_written_gets_no_configuration()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		harness.Coordinator.BeforeComplete = () =>
		{
			harness.Registry.SetEnabled(IntegrationId, false);
			return Task.CompletedTask;
		};

		await harness.ReportAsync("connection-1", device);

		Assert.Multiple(() =>
		{
			Assert.That(harness.Entries, Is.Empty);
			Assert.That(harness.Registry.IsExplicitlyDisabled(IntegrationId), Is.True);
		});
	}

	[Test]
	public async Task A_user_switching_off_right_after_the_configuration_was_written_keeps_it_switched_off()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		harness.Coordinator.AfterComplete = () =>
		{
			harness.Registry.SetEnabled(IntegrationId, false);
			return Task.CompletedTask;
		};

		await harness.ReportAsync("connection-1", device);

		Assert.Multiple(() =>
		{
			Assert.That(harness.Registry.IsExplicitlyDisabled(IntegrationId), Is.True);
			Assert.That(harness.Integration.IsInitialized, Is.False);
		});
	}

	[Test]
	public async Task Switching_the_integration_back_on_creates_entries_for_devices_already_connected()
	{
		var harness = new CompanionHarness(storedOff: true);
		var device = harness.AddDevice("Phone");
		await harness.ReportAsync("connection-1", device);

		harness.Registry.SetEnabled(IntegrationId, true);
		await harness.DeviceRegistry.CreationFor(device).WaitAsync(TimeSpan.FromSeconds(5));

		Assert.That(harness.Entries.Select(entry => entry.Id), Is.EqualTo(new[] { device }));
	}

	[Test]
	public async Task A_user_deleting_the_last_configuration_during_a_device_removal_keeps_it_switched_off()
	{
		var harness = new CompanionHarness();
		var removed = harness.AddDevice("Phone");
		var kept = harness.AddDevice("Tablet");
		await harness.ReportAsync("connection-1", removed);
		await harness.ReportAsync("connection-2", kept);
		harness.Coordinator.AfterDelete = async () =>
		{
			harness.Coordinator.AfterDelete = null;
			await harness.Coordinator.Inner.DeleteAsync(IntegrationId, kept, confirmed: true, CancellationToken.None);
		};

		await harness.DeviceRegistry.RemoveDeviceAsync(removed, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(harness.Entries, Is.Empty);
			Assert.That(harness.Registry.IsExplicitlyDisabled(IntegrationId), Is.True);
		});
	}

	[Test]
	public async Task A_failed_creation_is_tried_again_on_the_devices_next_report()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		harness.Coordinator.BeforeComplete = () =>
		{
			harness.Coordinator.BeforeComplete = null;
			throw new InvalidOperationException("store unavailable");
		};

		await harness.ReportAsync("connection-1", device);
		var entriesAfterFailure = harness.Entries.Count;
		await harness.ReportAsync("connection-1", device);

		Assert.Multiple(() =>
		{
			Assert.That(entriesAfterFailure, Is.Zero);
			Assert.That(harness.Entries.Select(entry => entry.Id), Is.EqualTo(new[] { device }));
		});
	}

	[Test]
	public async Task A_device_that_is_not_the_companion_app_gets_no_configuration()
	{
		var harness = new CompanionHarness();
		var browser = harness.AddDevice("Browser", DeviceClientType.WebClient);

		await harness.ReportAsync("connection-1", browser);

		Assert.That(harness.Entries, Is.Empty);
	}
}
