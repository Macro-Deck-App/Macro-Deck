using System.Collections.Concurrent;
using MacroDeck.Sdk.Batteries;
using MacroDeckHost.Application.Batteries;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Tests.UnitTests.Integrations;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Batteries;

[TestFixture]
internal sealed class BatteryRegistryTests
{
	private const string Owner = "com.example.batteries";

	private IntegrationRegistry _integrations = null!;
	private BatteryRegistry _registry = null!;
	private ConcurrentQueue<BatteryDevicesChangedEventArgs> _changes = null!;

	[SetUp]
	public void SetUp()
	{
		var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
		_integrations = new IntegrationRegistry(scopeFactory,
			new IntegrationLifecycleTests.FakeIntegrationStateStore(),
			Serilog.Core.Logger.None);
		_registry = new BatteryRegistry(TimeProvider.System, Serilog.Core.Logger.None, _integrations);
		_changes = new ConcurrentQueue<BatteryDevicesChangedEventArgs>();
		_registry.Changed += (_, args) => _changes.Enqueue(args);
	}

	[TearDown]
	public void TearDown()
	{
		_registry.Dispose();
	}

	[Test]
	public async Task A_published_device_is_listed_under_its_owner_and_can_be_found_by_its_qualified_id()
	{
		_registry.SetProviderName(Owner, "Example");
		_registry.Publish(Owner, "session-1", [Device("phone", 42)]);
		await WaitForChangeAsync();

		var device = _registry.Devices.Single();
		Assert.Multiple(() =>
		{
			Assert.That(device.Id, Is.EqualTo($"{Owner}:phone"));
			Assert.That(device.LocalId, Is.EqualTo("phone"));
			Assert.That(device.ProviderId, Is.EqualTo(Owner));
			Assert.That(device.ProviderName, Is.EqualTo("Example"));
			Assert.That(device.LevelPercent, Is.EqualTo(42));
			Assert.That(_registry.Find($"{Owner}:phone"), Is.EqualTo(device));
		});
	}

	[Test]
	public void Publishing_the_same_id_again_updates_the_device_instead_of_adding_one()
	{
		_registry.Publish(Owner, null, [Device("phone", 42)]);
		_registry.Publish(Owner, null, [Device("phone", 43)]);

		Assert.That(_registry.Devices.Select(device => device.LevelPercent), Is.EqualTo(new int?[] { 43 }));
	}

	[Test]
	public void A_batch_with_a_duplicate_id_is_refused_as_a_whole()
	{
		_registry.Publish(Owner, null, [Device("phone", 10)]);

		Assert.Multiple(() =>
		{
			Assert.Throws<ArgumentException>(() =>
				_registry.Publish(Owner, null, [Device("phone", 99), Device("tablet"), Device("tablet")]));
			Assert.That(_registry.Devices.Select(device => (device.LocalId, device.LevelPercent)),
				Is.EqualTo(new[] { ("phone", (int?)10) }));
		});
	}

	[Test]
	public void An_owner_can_have_at_most_64_devices_and_a_publish_beyond_that_applies_nothing()
	{
		_registry.Publish(Owner, null, [.. Enumerable.Range(0, 64).Select(index => Device($"d{index}"))]);

		Assert.Multiple(() =>
		{
			Assert.That(_registry.Devices, Has.Count.EqualTo(64));
			Assert.Throws<InvalidOperationException>(() =>
				_registry.Publish(Owner, null, [Device("d0", 1), Device("d64")]));
			Assert.That(_registry.Find($"{Owner}:d0")!.LevelPercent, Is.Null);
			Assert.That(_registry.Find($"{Owner}:d64"), Is.Null);
		});
	}

	[Test]
	public void The_cap_is_per_owner()
	{
		_registry.Publish(Owner, null, [.. Enumerable.Range(0, 64).Select(index => Device($"d{index}"))]);

		Assert.DoesNotThrow(() => _registry.Publish("com.example.other", null, [Device("d0")]));
	}

	[Test]
	public async Task Removing_a_device_reports_it_as_removed_for_good()
	{
		_registry.Publish(Owner, null, [Device("phone"), Device("watch")]);
		await WaitForChangeAsync();

		_registry.Remove(Owner, ["phone"]);
		var change = await WaitForChangeAsync(args => args.RemovedIds.Count > 0);

		Assert.Multiple(() =>
		{
			Assert.That(change.RemovedIds, Is.EqualTo(new[] { $"{Owner}:phone" }));
			Assert.That(_registry.Devices.Select(device => device.LocalId), Is.EqualTo(new[] { "watch" }));
		});
	}

	[Test]
	public async Task Sync_replaces_the_owners_set_and_reports_the_devices_left_out_as_removed()
	{
		_registry.Publish(Owner, "session-1", [Device("phone"), Device("watch")]);
		await WaitForChangeAsync();

		_registry.Sync(Owner, "session-1", [Device("watch", 80), Device("buds")]);
		var change = await WaitForChangeAsync(args => args.RemovedIds.Count > 0);

		Assert.Multiple(() =>
		{
			Assert.That(change.RemovedIds, Is.EqualTo(new[] { $"{Owner}:phone" }));
			Assert.That(_registry.Devices.Select(device => device.LocalId), Is.EquivalentTo(new[] { "watch", "buds" }));
		});
	}

	[Test]
	public void A_sync_with_an_invalid_set_applies_nothing()
	{
		_registry.Publish(Owner, null, [Device("phone")]);

		Assert.Multiple(() =>
		{
			Assert.Throws<ArgumentException>(() => _registry.Sync(Owner, null, [Device("watch"), Device("watch")]));
			Assert.Throws<InvalidOperationException>(() =>
				_registry.Sync(Owner, null, [.. Enumerable.Range(0, 65).Select(index => Device($"d{index}"))]));
			Assert.That(_registry.Devices.Select(device => device.LocalId), Is.EqualTo(new[] { "phone" }));
		});
	}

	[Test]
	public async Task Removing_all_of_an_owners_devices_withdraws_them_without_reporting_a_removal()
	{
		_registry.Publish(Owner, null, [Device("phone")]);
		await WaitForChangeAsync();
		_changes.Clear();

		_registry.RemoveAll(Owner);
		var change = await WaitForChangeAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_registry.Devices, Is.Empty);
			Assert.That(change.RemovedIds, Is.Empty);
		});
	}

	[Test]
	public void An_ended_session_withdraws_only_the_devices_it_published()
	{
		_registry.Publish(Owner, "old", [Device("phone")]);
		_registry.Publish(Owner, "new", [Device("watch")]);

		_registry.RemoveSession(Owner, "old");

		Assert.That(_registry.Devices.Select(device => device.LocalId), Is.EqualTo(new[] { "watch" }));
	}

	[Test]
	public void A_late_end_of_an_old_session_keeps_what_the_new_session_republished()
	{
		_registry.Publish(Owner, "old", [Device("phone", 10)]);
		_registry.Sync(Owner, "new", [Device("phone", 11)]);

		_registry.RemoveSession(Owner, "old");

		Assert.That(_registry.Find($"{Owner}:phone")?.LevelPercent, Is.EqualTo(11));
	}

	[Test]
	public async Task A_device_published_while_its_owner_is_disabled_appears_once_the_owner_is_enabled()
	{
		_integrations.SetEnabled(Owner, false);
		_registry.Publish(Owner, null, [Device("phone")]);

		Assert.That(_registry.Devices, Is.Empty);
		Assert.That(_registry.Find($"{Owner}:phone"), Is.Null);

		_integrations.SetEnabled(Owner, true);
		var change = await WaitForChangeAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_registry.Devices.Select(device => device.LocalId), Is.EqualTo(new[] { "phone" }));
			Assert.That(change.RemovedIds, Is.Empty);
		});
	}

	[Test]
	public async Task Disabling_an_owner_hides_its_devices_without_removing_them_and_enabling_shows_them_again()
	{
		_registry.Publish(Owner, null, [Device("phone")]);
		await WaitForChangeAsync();
		_changes.Clear();

		_integrations.SetEnabled(Owner, false);
		var hidden = await WaitForChangeAsync();
		var visibleWhileDisabled = _registry.Devices.Count;
		_changes.Clear();

		_integrations.SetEnabled(Owner, true);
		var shown = await WaitForChangeAsync();

		Assert.Multiple(() =>
		{
			Assert.That(visibleWhileDisabled, Is.Zero);
			Assert.That(hidden.RemovedIds, Is.Empty, "disabling withdraws the devices, it does not remove them");
			Assert.That(shown.RemovedIds, Is.Empty);
			Assert.That(_registry.Devices.Select(device => device.LocalId), Is.EqualTo(new[] { "phone" }));
		});
	}

	[Test]
	public async Task A_throwing_change_handler_does_not_keep_the_others_from_hearing_about_the_change()
	{
		_registry.Changed += (_, _) => throw new InvalidOperationException("boom");
		var heard = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		_registry.Changed += (_, _) => heard.TrySetResult();

		_registry.Publish(Owner, null, [Device("phone")]);

		Assert.That(async () => await heard.Task.WaitAsync(TimeSpan.FromSeconds(5)), Throws.Nothing);
	}

	private async Task<BatteryDevicesChangedEventArgs> WaitForChangeAsync(
		Func<BatteryDevicesChangedEventArgs, bool>? predicate = null)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (DateTime.UtcNow < deadline)
		{
			var match = _changes.FirstOrDefault(args => predicate?.Invoke(args) ?? true);
			if (match is not null)
			{
				return match;
			}

			await Task.Delay(10);
		}

		Assert.Fail("No battery change was raised.");
		return null!;
	}

	private static BatteryDevice Device(string id, int? level = null)
	{
		return new BatteryDevice { Id = id, Name = id, Kind = BatteryDeviceKinds.Phone, LevelPercent = level };
	}
}
