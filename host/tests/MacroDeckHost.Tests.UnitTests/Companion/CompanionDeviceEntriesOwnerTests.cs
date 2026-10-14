using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Companion;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins;

namespace MacroDeckHost.Tests.UnitTests.Companion;

[TestFixture]
internal sealed class CompanionDeviceEntriesOwnerTests
{
	private const string OwnerId = "com.example.devices";
	private const string PluginId = "com.example.plugin";

	[Test]
	public async Task An_integration_other_than_Companion_that_asks_for_device_entries_gets_one_per_device()
	{
		var harness = new CompanionHarness(register: false);
		await harness.Registry.RegisterAsync(new DeclaringIntegration(OwnerId));
		var device = harness.AddDevice("Stage phone");

		await harness.ReportAsync("connection-1", device);

		Assert.That(harness.Store.Entries.Select(entry => (entry.IntegrationId, entry.Id, entry.Title)),
			Is.EqualTo(new[] { (OwnerId, device, "Stage phone") }));
	}

	[Test]
	public async Task An_integration_that_does_not_ask_for_device_entries_gets_none()
	{
		var harness = new CompanionHarness(register: false);
		await harness.Registry.RegisterAsync(new PlainIntegration(OwnerId));
		var device = harness.AddDevice("Phone");

		await harness.ReportAsync("connection-1", device);

		Assert.That(harness.Store.Entries, Is.Empty);
	}

	[Test]
	public async Task The_first_integration_to_register_owns_the_entries()
	{
		var harness = new CompanionHarness();
		await harness.Registry.RegisterAsync(new DeclaringIntegration(OwnerId));
		var device = harness.AddDevice("Phone");

		await harness.ReportAsync("connection-1", device);

		Assert.That(harness.Store.Entries.Select(entry => entry.IntegrationId),
			Is.EqualTo(new[] { CompanionHarness.IntegrationId }));
	}

	[Test]
	public async Task A_built_in_integration_owns_the_entries_even_when_a_plugin_registered_first()
	{
		var access = new SwitchableAccess(true);
		var harness = new CompanionHarness(register: false, pluginAccess: access);
		await harness.Registry.RegisterAsync(
			new RemoteShapedIntegration(PluginId, CapabilityKinds.CompanionDeviceEntries),
			IntegrationOrigin.Plugin);
		await harness.Registry.RegisterAsync(harness.Integration);
		var device = harness.AddDevice("Phone");

		await harness.ReportAsync("connection-1", device);

		Assert.That(harness.Store.Entries.Select(entry => entry.IntegrationId),
			Is.EqualTo(new[] { CompanionHarness.IntegrationId }));
	}

	[Test]
	public async Task A_plugin_that_registers_before_the_built_ins_never_gets_entries_once_Companion_registers()
	{
		var access = new SwitchableAccess(true);
		var harness = new CompanionHarness(register: false, pluginAccess: access, builtInsRegistered: false);
		await harness.Registry.RegisterAsync(
			new RemoteShapedIntegration(PluginId, CapabilityKinds.CompanionDeviceEntries),
			IntegrationOrigin.Plugin);
		var device = harness.AddDevice("Phone");

		await harness.ReportAsync("connection-1", device);
		var entriesBeforeBuiltIns = harness.Store.Entries.Select(entry => entry.IntegrationId).ToList();
		await harness.Registry.RegisterAsync(harness.Integration);
		harness.BuiltInRegistration.Complete();
		await harness.DeviceRegistry.CreationFor(device).WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(entriesBeforeBuiltIns, Is.Empty);
			Assert.That(harness.Store.Entries.Select(entry => entry.IntegrationId),
				Is.EqualTo(new[] { CompanionHarness.IntegrationId }));
		});
	}

	[Test]
	public async Task A_switched_off_built_in_owner_leaves_devices_without_entries_even_when_a_plugin_asks_for_them()
	{
		var access = new SwitchableAccess(true);
		var harness = new CompanionHarness(true, pluginAccess: access);
		await harness.Registry.RegisterAsync(
			new RemoteShapedIntegration(PluginId, CapabilityKinds.CompanionDeviceEntries),
			IntegrationOrigin.Plugin);
		var device = harness.AddDevice("Phone");

		await harness.ReportAsync("connection-1", device);

		Assert.That(harness.Store.Entries, Is.Empty);
	}

	[Test]
	public async Task A_plugin_owner_gets_entries_only_once_it_may_use_Companion_devices()
	{
		var access = new SwitchableAccess(false);
		var harness = new CompanionHarness(register: false, pluginAccess: access);
		await harness.Registry.RegisterAsync(
			new RemoteShapedIntegration(PluginId, CapabilityKinds.CompanionDeviceEntries),
			IntegrationOrigin.Plugin);
		var device = harness.AddDevice("Phone");

		await harness.ReportAsync("connection-1", device);
		var entriesWithoutAccess = harness.Store.Entries.Count;
		access.Allow();
		await harness.DeviceRegistry.CreationFor(device).WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(entriesWithoutAccess, Is.Zero);
			Assert.That(harness.Store.Entries.Select(entry => (entry.IntegrationId, entry.Id)),
				Is.EqualTo(new[] { (PluginId, device) }));
		});
	}

	[Test]
	public async Task A_plugin_that_did_not_declare_device_entries_is_unaffected()
	{
		var access = new SwitchableAccess(true);
		var harness = new CompanionHarness(register: false, pluginAccess: access);
		await harness.Registry.RegisterAsync(new RemoteShapedIntegration(PluginId, CapabilityKinds.Actions),
			IntegrationOrigin.Plugin);
		var device = harness.AddDevice("Phone");

		await harness.ReportAsync("connection-1", device);

		Assert.That(harness.Store.Entries, Is.Empty);
	}

	[Test]
	public async Task Removing_the_device_deletes_the_owners_entry()
	{
		var harness = new CompanionHarness(register: false);
		await harness.Registry.RegisterAsync(new DeclaringIntegration(OwnerId));
		var device = harness.AddDevice("Phone");
		await harness.ReportAsync("connection-1", device);

		await harness.DeviceRegistry.RemoveDeviceAsync(device, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(harness.Store.Entries, Is.Empty);
			Assert.That(harness.Registry.IsExplicitlyDisabled(OwnerId), Is.False);
		});
	}

	private class PlainIntegration(string id) : IIntegration
	{
		public string Id => id;

		public LocalizedText Name => id;

		public string Version => "1.0.0";

		public IReadOnlyList<IActionDefinition> Actions => [];

		public bool IsInitialized { get; private set; }

		public Task InitializeAsync(IIntegrationContext context)
		{
			IsInitialized = true;
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			IsInitialized = false;
			return Task.CompletedTask;
		}
	}

	private sealed class DeclaringIntegration(string id) : PlainIntegration(id), ICompanionDeviceConfigEntries;

	// Shaped like the remote adapter: it implements the marker whatever the plugin declared.
	private sealed class RemoteShapedIntegration(string id, params string[] declaredKinds)
		: PlainIntegration(id), IDeclaredCapabilityKinds, ICompanionDeviceConfigEntries
	{
		public IReadOnlyList<string> DeclaredCapabilityKinds => declaredKinds;
	}

	private sealed class SwitchableAccess(bool allowed) : IPluginCompanionDeviceAccessPolicy
	{
		private volatile bool _allowed = allowed;

		public event Action? Changed;

		public void Allow()
		{
			_allowed = true;
			Changed?.Invoke();
		}

		public Task<PluginCompanionDeviceAccess> EvaluateAsync(
			string pluginId,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult(_allowed
				? PluginCompanionDeviceAccess.Available
				: PluginCompanionDeviceAccess.NotAllowed);
		}

		public bool CanBeGranted(string pluginId)
		{
			return true;
		}

		public Task<bool> GetAllowPluginsAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult(_allowed);
		}

		public Task SetAllowPluginsAsync(bool allow, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}
	}
}
