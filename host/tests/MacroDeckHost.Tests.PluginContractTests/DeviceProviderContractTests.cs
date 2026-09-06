using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities.DeviceProvider;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.DeviceProvider;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Devices;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Tests.PluginContractTests.Harness;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class DeviceProviderContractTests : CapabilityContractFixture
{
	private static DeclaredCapability Provider()
		=> new()
		{
			Kind = CapabilityKinds.DeviceProvider,
			LocalId = ProviderCapabilityId.LocalId,
			VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
		};

	private static DeviceDescriptor Deck()
		=> new("SERIAL-1",
			"Stream Deck XL",
			"Stream Deck XL",
			"Elgato",
			"com.example.contract::xl",
			new DeviceCapabilities { KeyCount = 32, DialCount = 4, SupportsImages = true });

	[Test]
	public async Task Declaring_the_capability_registers_an_adapter_the_validator_accepts()
	{
		var integration = await ConnectAsync([
				new DeviceProviderCapabilityHandler([new TestDeviceProviderIntegration("Stream Deck", [Deck()])],
					TestMetadata.Default)
			],
			[Provider()],
			[CapabilityKinds.DeviceProvider]);

		Assert.That(IntegrationCapabilityValidator.Validate(integration), Is.Empty);
	}

	[Test]
	public async Task Describe_round_trips_the_providers_name_and_devices()
	{
		await ConnectAsync([
				new DeviceProviderCapabilityHandler([new TestDeviceProviderIntegration("Stream Deck", [Deck()])],
					TestMetadata.Default)
			],
			[Provider()],
			[CapabilityKinds.DeviceProvider]);

		var data = await InvokeRawAsync(CapabilityKinds.DeviceProvider,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.DeviceProvider.Describe);

		var payload = data?.Deserialize<DeviceProviderDescribePayload>(PluginProtocolJson.Options);
		var device = payload!.Devices.Single();

		Assert.Multiple(() =>
		{
			Assert.That(payload.ProviderName, Is.EqualTo("Stream Deck"));
			Assert.That(device.Id, Is.EqualTo("SERIAL-1"));
			Assert.That(device.Model, Is.EqualTo("Stream Deck XL"));
			Assert.That(device.LayoutReference, Is.EqualTo("com.example.contract::xl"));
			Assert.That(device.Capabilities!.KeyCount, Is.EqualTo(32));
			Assert.That(device.Capabilities.DialCount, Is.EqualTo(4));
			Assert.That(device.Presence, Is.EqualTo(nameof(DevicePresence.Online)));
		});
	}

	[Test]
	public async Task A_connecting_plugins_devices_are_registered_with_the_host_without_it_being_asked_to()
	{
		await ConnectAsync([
				new DeviceProviderCapabilityHandler([new TestDeviceProviderIntegration("Stream Deck", [Deck()])],
					TestMetadata.Default)
			],
			[Provider()],
			[CapabilityKinds.DeviceProvider]);

		Assert.Multiple(() =>
		{
			Assert.That(DeviceRegistry.AssignedIdOf(PluginId, "SERIAL-1"), Is.Not.Null);
			Assert.That(DeviceRegistry.Devices[(PluginId, "SERIAL-1")].Manufacturer, Is.EqualTo("Elgato"));
		});
	}

	[Test]
	public async Task Every_device_provider_operation_is_recognised()
	{
		await ConnectAsync([
				new DeviceProviderCapabilityHandler([new TestDeviceProviderIntegration("Stream Deck", [Deck()])],
					TestMetadata.Default)
			],
			[Provider()],
			[CapabilityKinds.DeviceProvider]);

		foreach (var operation in CapabilityOperations.For(CapabilityKinds.DeviceProvider))
		{
			Assert.DoesNotThrowAsync(async () => await InvokeRawAsync(CapabilityKinds.DeviceProvider,
					ProviderCapabilityId.LocalId,
					operation),
				$"'{operation}' has no case in the handler.");
		}
	}

	private sealed class TestDeviceProviderIntegration(string providerName, IReadOnlyList<DeviceDescriptor> devices)
		: IPluginIntegration, IDeviceProvider
	{
		public IReadOnlyList<IActionDefinition> Actions { get; } = [];

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;

		public string ProviderName { get; } = providerName;

		public Task InitializeAsync(IDeviceProviderContext context, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		public IReadOnlyList<DeviceDescriptor> GetDevices() => devices;
	}
}
