using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities.LayoutProvider;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Layouts;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Plugins.Capabilities.Mapping;
using MacroDeckHost.Application.ScreenSavers;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.ScreenSavers;

public class DeviceScreenSaverSupportTests
{
	private const string GridRegionJson
		= """{"id":"grid","kind":"grid","grid":{"rows":3,"columns":5},"count":0,"extra":{}}""";

	[Test]
	public void A_device_without_a_layout_snapshot_supports_screen_savers()
	{
		Assert.That(DeviceScreenSaverSupport.IsSupported(Device((string?)null)), Is.True);
	}

	[Test]
	public void A_snapshot_that_no_longer_deserializes_supports_screen_savers()
	{
		Assert.That(DeviceScreenSaverSupport.IsSupported(Device("{not json")), Is.True);
	}

	[Test]
	public void A_layout_without_visuals_supports_screen_savers()
	{
		Assert.That(DeviceScreenSaverSupport.IsSupported(Device(Layout(null, null))), Is.True);
	}

	[Test]
	public void Visuals_that_leave_the_flag_unset_support_screen_savers()
	{
		var visuals = new LayoutVisualCapabilities { StaticIcons = true, CustomFolderViews = false };

		Assert.That(DeviceScreenSaverSupport.IsSupported(Device(Layout(visuals, visuals))), Is.True);
	}

	[Test]
	public void A_snapshot_written_before_the_flag_existed_supports_screen_savers()
	{
		var snapshot = $$$"""
						  {"id":"xl","name":"XL","regions":[{{{GridRegionJson}}}],
						  "capabilities":{"visuals":{"staticIcons":true,"customFolderViews":false},"extra":{}},"metadata":{}}
						  """;

		Assert.That(DeviceScreenSaverSupport.IsSupported(Device(snapshot)), Is.True);
	}

	[Test]
	public void A_layout_wide_no_holds_when_the_grid_region_declares_visuals_without_the_flag()
	{
		var layout = Layout(new LayoutVisualCapabilities { ScreenSavers = false },
			new LayoutVisualCapabilities { StaticIcons = true });

		Assert.That(DeviceScreenSaverSupport.IsSupported(Device(layout)), Is.False);
	}

	[Test]
	public void A_no_on_the_grid_region_holds_when_the_layout_says_nothing()
	{
		var layout = Layout(null, new LayoutVisualCapabilities { ScreenSavers = false });

		Assert.That(DeviceScreenSaverSupport.IsSupported(Device(layout)), Is.False);
	}

	[Test]
	public void The_grid_region_overrides_the_layout_wide_value()
	{
		var layout = Layout(new LayoutVisualCapabilities { ScreenSavers = false },
			new LayoutVisualCapabilities { ScreenSavers = true });

		Assert.That(DeviceScreenSaverSupport.IsSupported(Device(layout)), Is.True);
	}

	[Test]
	public void A_layout_with_several_grids_is_read_from_its_layout_wide_value()
	{
		var layout = new LayoutDescriptor("dual",
			"Dual",
			[Grid("left", null), Grid("right", null)],
			new LayoutCapabilities { Visuals = new LayoutVisualCapabilities { ScreenSavers = false } });

		Assert.That(DeviceScreenSaverSupport.IsSupported(Device(layout)), Is.False);
	}

	[Test]
	public void An_older_plugin_that_does_not_send_the_flag_keeps_screen_savers()
	{
		var json = $$$"""
					  {"id":"xl","name":"XL","regions":[{{{GridRegionJson}}}],
					  "capabilities":{"visuals":{"staticIcons":true,"borders":true},"extra":{}},"metadata":{}}
					  """;

		var layout = LayoutDescriptorMapper.ToDescriptor(
			JsonSerializer.Deserialize<LayoutDescriptorDto>(json, PluginProtocolJson.Options)!);

		Assert.Multiple(() =>
		{
			Assert.That(layout.Capabilities!.Visuals!.ScreenSavers, Is.Null);
			Assert.That(DeviceScreenSaverSupport.IsSupported(Device(layout)), Is.True);
		});
	}

	[Test]
	public void A_declared_no_arrives_from_the_wire_and_survives_the_stored_snapshot()
	{
		var json = $$$"""
					  {"id":"xl","name":"XL","regions":[{{{GridRegionJson}}}],
					  "capabilities":{"visuals":{"staticIcons":true,"screenSavers":false},"extra":{}},"metadata":{}}
					  """;

		var layout = LayoutDescriptorMapper.ToDescriptor(
			JsonSerializer.Deserialize<LayoutDescriptorDto>(json, PluginProtocolJson.Options)!);

		Assert.Multiple(() =>
		{
			Assert.That(layout.Capabilities!.Visuals!.ScreenSavers, Is.False);
			Assert.That(DeviceScreenSaverSupport.IsSupported(Device(layout)), Is.False);
		});
	}

	private static DeviceEntity Device(LayoutDescriptor layout)
	{
		return Device(LayoutSnapshotSerializer.Serialize(layout));
	}

	private static DeviceEntity Device(string? snapshot)
	{
		return new DeviceEntity
		{
			Id = Guid.NewGuid(),
			SecretHash = string.Empty,
			Name = "Stream Deck",
			ClientType = DeviceClientType.Provider,
			LayoutReference = "com.example.deck::xl",
			LayoutSnapshot = snapshot,
			CreatedAt = DateTime.UtcNow,
			LastSeenAt = DateTime.UtcNow
		};
	}

	private static LayoutDescriptor Layout(LayoutVisualCapabilities? layoutWide, LayoutVisualCapabilities? grid)
	{
		return new LayoutDescriptor("xl",
			"XL",
			[Grid("grid", grid)],
			layoutWide is null ? null : new LayoutCapabilities { Visuals = layoutWide });
	}

	private static LayoutRegion Grid(string id, LayoutVisualCapabilities? visuals)
	{
		return new LayoutRegion
		{
			Id = id, Kind = LayoutRegionKinds.Grid, Grid = new LayoutGrid { Rows = 3, Columns = 5 }, Visuals = visuals
		};
	}
}
