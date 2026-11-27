using MacroDeck.Plugin.Protocol.Capabilities.LayoutProvider;
using MacroDeck.Sdk.Layouts;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Plugins.Capabilities.Mapping;

namespace MacroDeckHost.Tests.UnitTests.Layouts;

public class LayoutWidgetSpanPersistenceTests
{
	[Test]
	public void ARegisteredLayout_KeepsItsWidgetSpanLimits()
	{
		var layout = LayoutDescriptorMapper.ToDescriptor(new LayoutDescriptorDto
		{
			Id = "mk2",
			Name = "Stream Deck MK.2",
			Regions =
			[
				new LayoutRegionDto
				{
					Id = "keys",
					Kind = LayoutRegionKinds.Grid,
					Grid = new LayoutGridDto { Rows = 3, Columns = 5, MaxWidgetColumns = 1, MaxWidgetRows = 1 }
				}
			]
		});

		var grid = layout.PrimaryGrid!.Grid!;

		Assert.Multiple(() =>
		{
			Assert.That(grid.MaxWidgetColumns, Is.EqualTo(1));
			Assert.That(grid.MaxWidgetRows, Is.EqualTo(1));
		});
	}

	[Test]
	public void AStoredSnapshot_RoundTripsTheLimits()
	{
		var layout = new LayoutDescriptor("mk2",
			"Stream Deck MK.2",
			[
				new LayoutRegion
				{
					Id = "keys",
					Kind = LayoutRegionKinds.Grid,
					Grid = new LayoutGrid { Rows = 3, Columns = 5, MaxWidgetColumns = 1, MaxWidgetRows = 2 }
				}
			]);

		var read = LayoutSnapshotSerializer.TryDeserialize(LayoutSnapshotSerializer.Serialize(layout),
			out var restored);

		Assert.Multiple(() =>
		{
			Assert.That(read, Is.True);
			Assert.That(restored.PrimaryGrid!.Grid!.MaxWidgetColumns, Is.EqualTo(1));
			Assert.That(restored.PrimaryGrid.Grid.MaxWidgetRows, Is.EqualTo(2));
		});
	}

	[Test]
	public void ASnapshotStoredBeforeTheLimitsExisted_ReadsAsUnconstrained()
	{
		const string snapshot = "{\"id\":\"mk2\",\"name\":\"Stream Deck MK.2\",\"regions\":[{\"id\":\"keys\","
			+ "\"kind\":\"grid\",\"grid\":{\"rows\":3,\"columns\":5,\"isConfigurable\":false}}]}";

		var read = LayoutSnapshotSerializer.TryDeserialize(snapshot, out var restored);

		Assert.Multiple(() =>
		{
			Assert.That(read, Is.True);
			Assert.That(restored.PrimaryGrid!.Grid!.MaxWidgetColumns, Is.Null);
			Assert.That(restored.PrimaryGrid.Grid.MaxWidgetRows, Is.Null);
		});
	}
}
