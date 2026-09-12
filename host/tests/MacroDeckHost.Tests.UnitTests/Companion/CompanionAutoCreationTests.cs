using System.Text.Json;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Integrations.Companion;
using MacroDeckHost.Integrations.Obs;
using MacroDeckHost.Localization;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Companion;

[TestFixture]
internal sealed class CompanionAutoCreationTests
{
	private const string IntegrationId = CompanionHarness.IntegrationId;

	[Test]
	public async Task
		First_report_with_no_stored_choice_creates_one_entry_titled_with_the_device_name_and_enables_the_integration()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Stage phone");
		var explicitlyDisabledBefore = harness.Registry.IsExplicitlyDisabled(IntegrationId);

		await harness.ReportAsync("connection-1", device);
		await harness.ReportAsync("connection-1", device);
		await harness.ReportAsync("connection-2", device);

		Assert.Multiple(() =>
		{
			Assert.That(explicitlyDisabledBefore, Is.False);
			Assert.That(harness.Entries, Has.Count.EqualTo(1));
			Assert.That(harness.Entries[0].Id, Is.EqualTo(device));
			Assert.That(harness.Entries[0].Title, Is.EqualTo("Stage phone"));
			Assert.That(harness.Registry.IsEnabled(IntegrationId), Is.True);
		});
	}

	[Test]
	public async Task Two_devices_get_one_entry_each()
	{
		var harness = new CompanionHarness();
		var first = harness.AddDevice("Phone");
		var second = harness.AddDevice("Tablet");

		await harness.ReportAsync("connection-1", first);
		await harness.ReportAsync("connection-2", second);

		Assert.That(harness.Entries.Select(entry => entry.Id), Is.EquivalentTo(new[] { first, second }));
	}

	[Test]
	public async Task No_entry_is_created_while_the_integration_is_explicitly_disabled()
	{
		var harness = new CompanionHarness(storedOff: true);
		var device = harness.AddDevice("Phone");

		await harness.ReportAsync("connection-1", device);

		Assert.Multiple(() =>
		{
			Assert.That(harness.Entries, Is.Empty);
			Assert.That(harness.Registry.IsExplicitlyDisabled(IntegrationId), Is.True);
		});
	}

	[Test]
	public async Task A_renamed_entry_keeps_its_title_when_the_device_reconnects()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		await harness.ReportAsync("connection-1", device);
		await harness.Coordinator.RenameAsync(IntegrationId, device, "Studio phone", CancellationToken.None);

		harness.DeviceRegistry.Disconnected("connection-1");
		await harness.ReportAsync("connection-2", device);

		Assert.Multiple(() =>
		{
			Assert.That(harness.Entries, Has.Count.EqualTo(1));
			Assert.That(harness.Entries[0].Title, Is.EqualTo("Studio phone"));
		});
	}

	[Test]
	public async Task A_report_after_the_device_row_was_removed_creates_no_entry()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		harness.RemoveDeviceRow(device);

		await harness.ReportAsync("connection-1", device);

		Assert.That(harness.Entries, Is.Empty);
	}

	[Test]
	public async Task The_report_returns_while_entry_creation_is_blocked()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		harness.Coordinator.BeforeComplete = () => release.Task;

		harness.DeviceRegistry.Report("connection-1", device, CompanionHarness.Report());
		var stateVisible = harness.DeviceRegistry.TryGetState(device, out _);
		var entriesWhileBlocked = harness.Entries.Count;

		release.SetResult();
		await harness.DeviceRegistry.CreationFor(device).WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(stateVisible, Is.True);
			Assert.That(entriesWhileBlocked, Is.Zero);
			Assert.That(harness.Entries, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_colliding_user_variable_gives_the_configuration_a_suffixed_key()
	{
		var harness = new CompanionHarness(suffixFactory: () => 1234);
		await harness.Variables.CreateUserVariable("companion_phone_is_connected",
			VariableScope.Global,
			null,
			VariableType.Boolean,
			null,
			null);
		var device = harness.AddDevice("Phone");

		await harness.ReportAsync("connection-1", device);

		var entry = harness.Entries.Single();
		var parsed = ObsConfigurationMetadata.TryParseIdentity(
			entry.Values[CompanionConfigurationMetadata.VariableIdentityKey].GetString(),
			out var identity);
		var variables = await harness.Variables.GetByOwnerIntegration(IntegrationId);
		Assert.Multiple(() =>
		{
			Assert.That(parsed, Is.True);
			Assert.That(identity.Key, Is.EqualTo("phone_1234"));
			Assert.That(variables, Has.Count.EqualTo(8));
			Assert.That(variables.All(variable =>
					variable.Name.StartsWith("companion_phone_1234_", StringComparison.Ordinal)),
				Is.True);
		});
	}

	[Test]
	public async Task Exhausting_every_suffix_fails_preparation_with_the_localized_error()
	{
		var harness = new CompanionHarness(suffixFactory: () => 1234);
		foreach (var name in new[] { "companion_phone_is_connected", "companion_phone_1234_is_connected" })
		{
			await harness.Variables.CreateUserVariable(name,
				VariableScope.Global,
				null,
				VariableType.Boolean,
				null,
				null);
		}

		var outcome = await harness.Coordinator.CompleteAsync(IntegrationId,
			Guid.NewGuid(),
			"Phone",
			new Dictionary<string, JsonElement>(),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Success, Is.False);
			Assert.That(TestLocalization.Resolve(outcome.Error),
				Is.EqualTo(TestLocalization.Resolve(
						AppStrings.Integrations.Companion.Config.VariableIdentityExhausted()))
					.And.Not.Empty);
			Assert.That(harness.Entries, Is.Empty);
		});
	}
}
