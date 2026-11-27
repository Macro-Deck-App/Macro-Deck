using System.Text.Json.Nodes;
using MacroDeck.Plugin.Protocol.Capabilities.LayoutProvider;
using MacroDeck.Sdk.Layouts;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Plugins.Capabilities.Mapping;

namespace MacroDeckHost.Tests.UnitTests.Layouts;

[TestFixture]
public class LayoutKeyImageSnapshotTests
{
	private static readonly LayoutKeyImageFormat _bmp = new()
	{
		Encoding = KeyImageEncodings.Bmp, Rotation = 90, MirrorHorizontal = true, CornerRadius = 9
	};

	[Test]
	public void A_registered_grid_keeps_the_key_image_format_it_asked_for()
	{
		var layout = LayoutDescriptorMapper.ToDescriptor(new LayoutDescriptorDto
		{
			Id = "deck",
			Name = "Deck",
			Regions =
			[
				new LayoutRegionDto
				{
					Id = "keys",
					Kind = LayoutRegionKinds.Grid,
					Grid = new LayoutGridDto
					{
						Rows = 3,
						Columns = 5,
						KeySize = new LayoutKeySizeDto { Width = 72, Height = 72 },
						KeyImages = new LayoutKeyImageFormatDto
						{
							Encoding = KeyImageEncodings.Bmp, Rotation = 90, MirrorHorizontal = true, CornerRadius = 9
						}
					}
				}
			]
		});

		Assert.That(layout.PrimaryGrid!.Grid!.KeyImages, Is.EqualTo(_bmp));
	}

	[Test]
	public void A_snapshot_keeps_the_format_and_one_written_before_key_images_reads_as_asking_for_none()
	{
		var current = LayoutSnapshotSerializer.Serialize(Layout(_bmp));
		var beforeKeyImages = JsonNode.Parse(LayoutSnapshotSerializer.Serialize(Layout(null)))!;
		beforeKeyImages["regions"]![0]!["grid"]!.AsObject().Remove("keyImages");
		beforeKeyImages["primaryGrid"]?["grid"]?.AsObject().Remove("keyImages");

		var readCurrent = LayoutSnapshotSerializer.TryDeserialize(current, out var withFormat);
		var readOld = LayoutSnapshotSerializer.TryDeserialize(beforeKeyImages.ToJsonString(), out var withoutFormat);

		Assert.Multiple(() =>
		{
			Assert.That(readCurrent, Is.True);
			Assert.That(withFormat.PrimaryGrid!.Grid!.KeyImages, Is.EqualTo(_bmp));
			Assert.That(beforeKeyImages.ToJsonString(), Does.Not.Contain("keyImages"));
			Assert.That(readOld, Is.True);
			Assert.That(withoutFormat.PrimaryGrid!.Grid!.Rows, Is.EqualTo(3));
			Assert.That(withoutFormat.PrimaryGrid.Grid.KeyImages, Is.Null);
		});
	}

	private static LayoutDescriptor Layout(LayoutKeyImageFormat? keyImages)
	{
		return new LayoutDescriptor("plugin::deck",
			"Deck",
			[
				new LayoutRegion
				{
					Id = "keys",
					Kind = LayoutRegionKinds.Grid,
					Grid = new LayoutGrid
					{
						Rows = 3, Columns = 5, KeySize = new LayoutKeySize(72, 72), KeyImages = keyImages
					}
				}
			]);
	}
}
