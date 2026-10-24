using System.Reflection;
using System.Text.Json;
using MacroDeck.Sdk.Batteries;
using MacroDeck.Sdk.Companion;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Infrastructure;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Integrations.Companion;
using MacroDeckHost.Integrations.Companion.Providers;
using MacroDeckHost.Tests.UnitTests.Adb;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Companion;

[TestFixture]
internal sealed class CompanionBatteryProviderTests
{
	[Test]
	public async Task A_connected_device_reporting_a_level_is_published_under_its_entry_title()
	{
		var (harness, device) = await WithEntryAsync("Living Room Phone");
		var devices = new ScriptedCompanionDevices();
		devices.Connect(device,
			new CompanionDeviceState { BatteryLevelPercent = 57, IsCharging = true, Model = "iPhone16,1" });
		var (integration, batteries) = await StartAsync(harness, devices);

		var published = batteries.Latest(CompanionBatteryProvider.IdFor(device));
		await integration.ShutdownAsync();

		Assert.That(published,
			Is.EqualTo(new BatteryDevice
			{
				Id = CompanionBatteryProvider.IdFor(device),
				Name = "Living Room Phone",
				Kind = BatteryDeviceKinds.Phone,
				LevelPercent = 57,
				ChargeState = BatteryChargeState.Charging,
				IsConnected = true
			}));
	}

	[Test]
	public async Task The_published_id_does_not_reveal_the_companion_device_id_and_stays_the_same_across_reconnects()
	{
		var (harness, device) = await WithEntryAsync("Phone");
		var devices = new ScriptedCompanionDevices();
		var (integration, batteries) = await StartAsync(harness, devices);
		var id = CompanionBatteryProvider.IdFor(device);

		devices.Connect(device, new CompanionDeviceState { BatteryLevelPercent = 40 });
		await WaitUntil(() => batteries.Latest(id) is { IsConnected: true });
		devices.Disconnect(device);
		await WaitUntil(() => batteries.Latest(id) is { IsConnected: false });
		devices.Connect(device, new CompanionDeviceState { BatteryLevelPercent = 38 });
		await WaitUntil(() => batteries.Latest(id) is { IsConnected: true, LevelPercent: 38 });
		await integration.ShutdownAsync();

		var ids = batteries.Publishes.SelectMany(batch => batch).Select(published => published.Id).Distinct();
		Assert.Multiple(() =>
		{
			Assert.That(ids, Is.EqualTo(new[] { id }));
			Assert.That(id, Does.Not.Contain(device.ToString("N")).IgnoreCase);
			Assert.That(id, Does.Not.Contain(device.ToString("D")).IgnoreCase);
		});
	}

	[Test]
	public async Task A_state_report_updates_the_level_and_charge_state()
	{
		var (harness, device) = await WithEntryAsync("Phone");
		var devices = new ScriptedCompanionDevices();
		devices.Connect(device, new CompanionDeviceState { BatteryLevelPercent = 99, IsCharging = true });
		var (integration, batteries) = await StartAsync(harness, devices);
		var id = CompanionBatteryProvider.IdFor(device);

		devices.Report(device, new CompanionDeviceState { BatteryLevelPercent = 100, IsCharging = true });
		await WaitUntil(() => batteries.Latest(id)?.LevelPercent == 100);
		var full = batteries.Latest(id);
		devices.Report(device, new CompanionDeviceState { BatteryLevelPercent = 97, IsCharging = false });
		await WaitUntil(() => batteries.Latest(id)?.LevelPercent == 97);
		var unplugged = batteries.Latest(id);
		await integration.ShutdownAsync();

		Assert.Multiple(() =>
		{
			Assert.That(full!.ChargeState, Is.EqualTo(BatteryChargeState.Full));
			Assert.That(unplugged!.ChargeState, Is.EqualTo(BatteryChargeState.Discharging));
		});
	}

	[Test]
	public async Task A_disconnected_device_stays_published_as_not_connected_with_its_last_level()
	{
		var (harness, device) = await WithEntryAsync("Phone");
		var devices = new ScriptedCompanionDevices();
		devices.Connect(device, new CompanionDeviceState { BatteryLevelPercent = 21 });
		var (integration, batteries) = await StartAsync(harness, devices);
		var id = CompanionBatteryProvider.IdFor(device);

		devices.Disconnect(device);
		await WaitUntil(() => batteries.Latest(id) is { IsConnected: false });
		var gone = batteries.Latest(id);
		await integration.ShutdownAsync();

		Assert.Multiple(() =>
		{
			Assert.That(gone!.LevelPercent, Is.EqualTo(21));
			Assert.That(batteries.Removed, Is.Empty);
		});
	}

	[Test]
	public async Task A_device_that_never_reports_a_level_is_not_offered()
	{
		var (harness, device) = await WithEntryAsync("Desktop");
		var devices = new ScriptedCompanionDevices();
		devices.Connect(device, new CompanionDeviceState { Model = "Mac" });
		var (integration, batteries) = await StartAsync(harness, devices);

		devices.Report(device, new CompanionDeviceState { Model = "Mac", IsCharging = true });
		await Task.Delay(100);
		await integration.ShutdownAsync();

		Assert.That(batteries.Publishes, Is.Empty);
	}

	[Test]
	public async Task A_device_that_reports_before_its_entry_exists_is_published_once_the_entry_is_stored()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("New Phone");
		var devices = new ScriptedCompanionDevices();
		var (integration, batteries) = await StartAsync(harness, devices);

		devices.Connect(device, new CompanionDeviceState { BatteryLevelPercent = 64 });
		await Task.Delay(100);
		var beforeEntry = batteries.Publishes.Count;
		await harness.ReportAsync("connection-1", device);
		await harness.Integration.ShutdownAsync();
		await integration.ReloadEntriesAsync();
		await WaitUntil(() => batteries.Latest(CompanionBatteryProvider.IdFor(device)) is not null);
		var published = batteries.Latest(CompanionBatteryProvider.IdFor(device));
		await integration.ShutdownAsync();

		Assert.Multiple(() =>
		{
			Assert.That(beforeEntry, Is.Zero);
			Assert.That(published!.Name, Is.EqualTo("New Phone"));
			Assert.That(published.LevelPercent, Is.EqualTo(64));
		});
	}

	[Test]
	public async Task Renaming_the_entry_renames_the_device()
	{
		var (harness, device) = await WithEntryAsync("Phone");
		var devices = new ScriptedCompanionDevices();
		devices.Connect(device, new CompanionDeviceState { BatteryLevelPercent = 50 });
		var (integration, batteries) = await StartAsync(harness, devices);
		var entry = harness.Entries.Single();

		await harness.Store.Replace(entry.Id, "Kitchen Tablet", entry.Values);
		await integration.ReloadEntriesAsync();
		await WaitUntil(() => batteries.Latest(CompanionBatteryProvider.IdFor(device))?.Name == "Kitchen Tablet");
		await integration.ShutdownAsync();

		Assert.That(batteries.Removed, Is.Empty);
	}

	[Test]
	public async Task Deleting_the_entry_removes_the_device_for_good()
	{
		var (harness, device) = await WithEntryAsync("Phone");
		var devices = new ScriptedCompanionDevices();
		devices.Connect(device, new CompanionDeviceState { BatteryLevelPercent = 50 });
		var (integration, batteries) = await StartAsync(harness, devices);

		await harness.Store.Delete(harness.Entries.Single().Id);
		await integration.ReloadEntriesAsync();
		await WaitUntil(() => batteries.Removed.Count > 0);
		await integration.ShutdownAsync();

		Assert.That(batteries.Removed, Is.EqualTo(new[] { CompanionBatteryProvider.IdFor(device) }));
	}

	[Test]
	public async Task An_entry_that_needs_reconfiguration_keeps_its_device_as_not_connected()
	{
		var (harness, device) = await WithEntryAsync("Phone");
		var devices = new ScriptedCompanionDevices();
		devices.Connect(device, new CompanionDeviceState { BatteryLevelPercent = 50 });
		var (integration, batteries) = await StartAsync(harness, devices);
		var entry = harness.Entries.Single();

		await harness.Store.Replace(entry.Id, entry.Title, new Dictionary<string, JsonElement>());
		await integration.ReloadEntriesAsync();
		await WaitUntil(() => batteries.Latest(CompanionBatteryProvider.IdFor(device)) is { IsConnected: false });
		await integration.ShutdownAsync();

		Assert.That(batteries.Removed, Is.Empty);
	}

	[Test]
	public async Task Shutting_the_integration_down_before_its_provider_removes_nothing()
	{
		var (harness, device) = await WithEntryAsync("Phone");
		var devices = new ScriptedCompanionDevices();
		devices.Connect(device, new CompanionDeviceState { BatteryLevelPercent = 50 });
		var (integration, batteries) = await StartAsync(harness, devices);

		await integration.ShutdownAsync();
		devices.Report(device, new CompanionDeviceState { BatteryLevelPercent = 49 });
		await Task.Delay(100);
		await ((IBatteryProvider)integration).ShutdownAsync();

		Assert.That(batteries.Removed, Is.Empty);
	}

	[Test]
	public async Task A_stopped_provider_publishes_nothing_more()
	{
		var (harness, device) = await WithEntryAsync("Phone");
		var devices = new ScriptedCompanionDevices();
		devices.Connect(device, new CompanionDeviceState { BatteryLevelPercent = 50 });
		var (integration, batteries) = await StartAsync(harness, devices);
		var before = batteries.Publishes.Count;

		await ((IBatteryProvider)integration).ShutdownAsync();
		devices.Report(device, new CompanionDeviceState { BatteryLevelPercent = 10 });
		devices.Disconnect(device);
		await Task.Delay(100);
		await integration.ShutdownAsync();

		Assert.That(batteries.Publishes, Has.Count.EqualTo(before));
	}

	[TestCase("Google Pixel 8", "Android 14", CompanionFormFactors.Phone, BatteryDeviceKinds.Phone)]
	[TestCase("samsung SM-X200", "Android 14", CompanionFormFactors.Tablet, BatteryDeviceKinds.Tablet)]
	[TestCase("iPad13,4", "iPadOS 18.2", CompanionFormFactors.Tablet, BatteryDeviceKinds.Tablet)]
	[TestCase("iPhone16,1", "iOS 18.2", CompanionFormFactors.Phone, BatteryDeviceKinds.Phone)]
	[TestCase("iPad13,4", "iPadOS 18.2", null, BatteryDeviceKinds.Tablet)]
	[TestCase("iPad", "iOS 18.2", null, BatteryDeviceKinds.Tablet)]
	[TestCase("iPhone16,1", "iOS 18.2", null, BatteryDeviceKinds.Phone)]
	[TestCase(null, "iOS 18.2", null, BatteryDeviceKinds.Phone)]
	[TestCase("Google Pixel 8", "Android 14", null, BatteryDeviceKinds.Other)]
	[TestCase("Google Pixel 8", "Android 14", CompanionFormFactors.Desktop, BatteryDeviceKinds.Other)]
	[TestCase("Google Pixel 8", "Android 14", "watch", BatteryDeviceKinds.Other)]
	[TestCase(null, null, null, BatteryDeviceKinds.Other)]
	public async Task The_form_factor_decides_the_kind_and_without_one_an_ipad_is_a_tablet_and_an_iphone_a_phone(
		string? model,
		string? platform,
		string? formFactor,
		string kind)
	{
		var (harness, device) = await WithEntryAsync("Device");
		var devices = new ScriptedCompanionDevices();
		devices.Connect(device,
			new CompanionDeviceState
				{ BatteryLevelPercent = 50, Model = model, Platform = platform, FormFactor = formFactor });
		var (integration, batteries) = await StartAsync(harness, devices);

		var published = batteries.Latest(CompanionBatteryProvider.IdFor(device));
		await integration.ShutdownAsync();

		Assert.That(published!.Kind, Is.EqualTo(kind));
	}

	[Test]
	public void The_provider_reaches_the_host_only_through_sdk_types_and_its_own_integration()
	{
		var reached = new HashSet<Type>();
		var pending = new Stack<Type>([typeof(CompanionBatteryProvider)]);
		while (pending.TryPop(out var type))
		{
			foreach (var nested in type.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
			{
				pending.Push(nested);
			}

			const BindingFlags all = BindingFlags.Public
				| BindingFlags.NonPublic
				| BindingFlags.Instance
				| BindingFlags.Static
				| BindingFlags.DeclaredOnly;
			var referenced = type.GetFields(all)
				.Select(field => field.FieldType)
				.Concat(type.GetMethods(all)
					.SelectMany(method => method.GetParameters()
						.Select(parameter => parameter.ParameterType)
						.Append(method.ReturnType)))
				.Concat(type.GetConstructors(all)
					.SelectMany(constructor => constructor.GetParameters()
						.Select(parameter => parameter.ParameterType)));
			foreach (var reference in referenced.SelectMany(Flatten))
			{
				reached.Add(reference);
			}
		}

		var hostTypes = reached
			.Where(type => type.Namespace?.StartsWith("MacroDeckHost", StringComparison.Ordinal) == true)
			.Where(type => type.Namespace?.StartsWith("MacroDeckHost.Integrations.Companion", StringComparison.Ordinal)
				!= true)
			.ToList();

		Assert.That(hostTypes, Is.Empty);
	}

	private static IEnumerable<Type> Flatten(Type type)
	{
		if (type.HasElementType)
		{
			return Flatten(type.GetElementType()!);
		}

		return type.IsGenericType
			? type.GetGenericArguments().SelectMany(Flatten).Prepend(type.GetGenericTypeDefinition())
			: [type];
	}

	private static async Task<(CompanionIntegration Integration, RecordingBatteryProviderContext Batteries)>
		StartAsync(CompanionHarness harness, ScriptedCompanionDevices devices)
	{
		var integration = new CompanionIntegration();
		var batteries = new RecordingBatteryProviderContext();
		await integration.InitializeAsync(Context(harness, devices));
		await ((IBatteryProvider)integration).InitializeAsync(batteries);
		return (integration, batteries);
	}

	private static async Task<(CompanionHarness Harness, Guid Device)> WithEntryAsync(string name)
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice(name);
		await harness.ReportAsync("connection-1", device);
		harness.DeviceRegistry.Disconnected("connection-1");
		await harness.Integration.ShutdownAsync();
		return (harness, device);
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

	private static async Task WaitUntil(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
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
