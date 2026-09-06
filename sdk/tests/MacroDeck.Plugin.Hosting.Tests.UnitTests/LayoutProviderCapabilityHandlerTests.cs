using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.LayoutProvider;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.LayoutProvider;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Layouts;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class LayoutProviderCapabilityHandlerTests
{
	private static ServiceProvider _services = null!;

	[OneTimeSetUp]
	public static void OneTimeSetUp() => _services = new ServiceCollection().BuildServiceProvider();

	[OneTimeTearDown]
	public static void OneTimeTearDown() => _services.Dispose();

	private static CapabilityInvocation Invocation(string localId, string operation)
		=> new()
		{
			Kind = CapabilityKinds.LayoutProvider,
			LocalId = localId,
			Operation = operation,
			CorrelationId = "correlation",
			Services = _services
		};

	[Test]
	public void A_provider_declares_exactly_one_provider_local_id()
	{
		var integration = new TestLayoutIntegration("Stream Deck", [FullyPopulatedLayout()]);
		var handler = new LayoutProviderCapabilityHandler([integration], TestMetadata.Default);

		var declared = handler.DeclareCapabilities();

		Assert.Multiple(() =>
		{
			Assert.That(declared, Has.Count.EqualTo(1));
			Assert.That(declared[0].LocalId, Is.EqualTo(ProviderCapabilityId.LocalId));
			Assert.That(declared[0].Kind, Is.EqualTo(CapabilityKinds.LayoutProvider));
		});
	}

	[Test]
	public void An_integration_that_is_not_a_layout_provider_declares_nothing()
		=> Assert.That(new LayoutProviderCapabilityHandler([new TestIntegration()], TestMetadata.Default)
				.DeclareCapabilities(),
			Is.Empty);

	[Test]
	public async Task Describe_reports_the_provider_name_and_layouts()
	{
		var integration = new TestLayoutIntegration("Stream Deck", [FullyPopulatedLayout()]);
		var handler = new LayoutProviderCapabilityHandler([integration], TestMetadata.Default);

		var result = await handler.InvokeAsync(
			Invocation(ProviderCapabilityId.LocalId, CapabilityOperations.LayoutProvider.Describe),
			CancellationToken.None);

		Assert.That(result.IsFailure, Is.False);
		var payload = result.Data!.Value.Deserialize<LayoutProviderDescribePayload>(PluginProtocolJson.Options);
		Assert.Multiple(() =>
		{
			Assert.That(payload!.ProviderName, Is.EqualTo("Stream Deck"));
			Assert.That(payload.Layouts.Single().Id, Is.EqualTo("xl"));
		});
	}

	[Test]
	public async Task An_unknown_local_id_fails_with_capability_unavailable()
	{
		var handler = new LayoutProviderCapabilityHandler([new TestLayoutIntegration("Stream Deck", [])],
			TestMetadata.Default);

		var result = await handler.InvokeAsync(
			Invocation("not-the-provider", CapabilityOperations.LayoutProvider.Layouts),
			CancellationToken.None);

		Assert.That(result.IsFailure, Is.True);
		Assert.That(result.Error!.Code, Is.EqualTo(Protocol.Errors.ProtocolErrorCodes.CapabilityUnavailable));
	}

	[Test]
	public async Task A_fully_populated_layout_round_trips_through_the_wire_unchanged()
	{
		var original = FullyPopulatedLayout();

		var dto = LayoutDescriptorMapper.ToDto(original);
		var restored = LayoutDescriptorMapper.ToSdk(dto);

		Assert.Multiple(() =>
		{
			Assert.That(restored.Id, Is.EqualTo(original.Id));
			Assert.That(restored.Name, Is.EqualTo(original.Name));
			Assert.That(restored.Regions, Has.Count.EqualTo(original.Regions.Count));
			Assert.That(restored.Capabilities, Is.Not.Null);
			Assert.That(restored.Capabilities!.Visuals, Is.EqualTo(original.Capabilities!.Visuals));
			Assert.That(restored.Capabilities.Extra, Is.EqualTo(original.Capabilities.Extra));
			Assert.That(restored.Metadata, Is.EqualTo(original.Metadata));

			var grid = restored.Regions.Single(region => region.Kind == LayoutRegionKinds.Grid);
			Assert.That(grid.Grid, Is.EqualTo(original.Regions.Single(region => region.Id == grid.Id).Grid));
		});
	}

	[Test]
	public void A_grid_plus_encoder_and_pedal_regions_round_trip_with_the_grid_as_primary()
	{
		var original = new LayoutDescriptor("mixed",
			"Mixed Surface",
			[
				new LayoutRegion
				{
					Id = "main-grid", Kind = LayoutRegionKinds.Grid, Grid = new LayoutGrid { Rows = 3, Columns = 5 }
				},
				new LayoutRegion { Id = "dials", Kind = LayoutRegionKinds.Encoder, Count = 4 },
				new LayoutRegion { Id = "footswitches", Kind = LayoutRegionKinds.Pedal, Count = 2 }
			]);

		var restored = LayoutDescriptorMapper.ToSdk(LayoutDescriptorMapper.ToDto(original));

		Assert.Multiple(() =>
		{
			Assert.That(restored.Regions, Has.Count.EqualTo(3));
			Assert.That(restored.Regions.Select(region => region.Kind),
				Is.EquivalentTo(new[] { LayoutRegionKinds.Grid, LayoutRegionKinds.Encoder, LayoutRegionKinds.Pedal }));
			Assert.That(restored.PrimaryGrid, Is.Not.Null);
			Assert.That(restored.PrimaryGrid!.Id, Is.EqualTo("main-grid"));

			var encoders = restored.Regions.Single(region => region.Id == "dials");
			Assert.That(encoders.Count, Is.EqualTo(4));

			var pedals = restored.Regions.Single(region => region.Id == "footswitches");
			Assert.That(pedals.Count, Is.EqualTo(2));
		});
	}

	[Test]
	public void A_layout_of_entirely_unknown_region_kinds_round_trips_with_no_primary_grid()
	{
		var original = new LayoutDescriptor("odd",
			"Odd Surface",
			[
				new LayoutRegion
				{
					Id = "ring",
					Kind = "jog-wheel",
					Name = "Jog Wheel",
					Count = 1,
					Extra = new Dictionary<string, string>(StringComparer.Ordinal) { ["diameter-mm"] = "40" }
				}
			]);

		var restored = LayoutDescriptorMapper.ToSdk(LayoutDescriptorMapper.ToDto(original));

		Assert.Multiple(() =>
		{
			Assert.That(restored.Regions, Has.Count.EqualTo(1));

			var region = restored.Regions[0];
			Assert.That(region.Id, Is.EqualTo("ring"));
			Assert.That(region.Kind, Is.EqualTo("jog-wheel"));
			Assert.That(region.Name, Is.EqualTo("Jog Wheel"));
			Assert.That(region.Extra["diameter-mm"], Is.EqualTo("40"));
			Assert.That(restored.PrimaryGrid, Is.Null);
		});
	}

	[Test]
	public void Null_capabilities_visuals_grid_and_key_size_stay_null_rather_than_becoming_defaults()
	{
		var original = new LayoutDescriptor("bare",
			"Bare Surface",
			[new LayoutRegion { Id = "buttons", Kind = LayoutRegionKinds.Button, Count = 8 }]);

		var restored = LayoutDescriptorMapper.ToSdk(LayoutDescriptorMapper.ToDto(original));

		Assert.Multiple(() =>
		{
			Assert.That(restored.Capabilities, Is.Null);
			Assert.That(restored.Regions[0].Grid, Is.Null);
			Assert.That(restored.Regions[0].Visuals, Is.Null);
		});
	}

	private static LayoutDescriptor FullyPopulatedLayout()
		=> new("xl",
			"Stream Deck XL",
			[
				new LayoutRegion
				{
					Id = "grid",
					Kind = LayoutRegionKinds.Grid,
					Name = "Main Grid",
					Grid = new LayoutGrid
					{
						Rows = 4,
						Columns = 8,
						IsConfigurable = true,
						MinRows = 2,
						MaxRows = 4,
						MinColumns = 4,
						MaxColumns = 8,
						SupportsRuntimeResize = true,
						KeySize = new LayoutKeySize(96, 96)
					},
					Visuals = LayoutVisualCapabilities.Full,
					Extra = new Dictionary<string, string>(StringComparer.Ordinal) { ["firmware"] = "1.2.3" }
				}
			],
			new LayoutCapabilities
			{
				Visuals = LayoutVisualCapabilities.Full,
				Extra = new Dictionary<string, string>(StringComparer.Ordinal) { ["vendor"] = "Elgato" }
			},
			new Dictionary<string, string>(StringComparer.Ordinal) { ["serial"] = "abc123" });

	private sealed class TestLayoutIntegration(string providerName, IReadOnlyList<LayoutDescriptor> layouts)
		: ILayoutProvider, IPluginIntegration
	{
		public string ProviderName => providerName;

		public IReadOnlyList<IActionDefinition> Actions => [];

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;

		public Task InitializeAsync(ILayoutProviderContext context, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public IReadOnlyList<LayoutDescriptor> GetLayouts() => layouts;
	}
}
