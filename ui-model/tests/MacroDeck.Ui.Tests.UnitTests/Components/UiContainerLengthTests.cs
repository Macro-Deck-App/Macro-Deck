using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Ui.Tests.UnitTests.Components;

[TestFixture]
public class UiContainerLengthTests
{
	private const int MaxTreeBytes = 192 * 1024;

	private static UiTree Build(params UiElement[] children)
		=> UiViewBuilder.Build(
			new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
			new UiStack { Key = "root", Children = children });

	[Test]
	public void A_length_relative_to_the_parent_travels_with_the_basis_an_older_reader_uses()
	{
		var json = UiCanonicalJson.Serialize(UiLength.OfParent(0.085, 0.02));

		Assert.That(json, Is.EqualTo("""{"basis":0.02,"ofParent":0.085}"""));
	}

	[Test]
	public void The_single_argument_form_falls_back_to_the_same_fraction_of_the_widget_basis()
	{
		var json = UiCanonicalJson.Serialize(UiLength.OfParent(0.085));

		Assert.That(json, Is.EqualTo("""{"basis":0.085,"ofParent":0.085}"""));
	}

	[Test]
	public void A_length_without_the_member_is_serialized_exactly_as_before()
	{
		Assert.Multiple(() =>
		{
			Assert.That(UiCanonicalJson.Serialize(UiLength.OfBasis(0.2)), Is.EqualTo("""{"basis":0.2}"""));
			Assert.That(UiCanonicalJson.Serialize(UiLength.OfBasis(0.2, 0.5)), Is.EqualTo("""{"basis":0.2,"maxOfCross":0.5}"""));
			Assert.That(UiCanonicalJson.Serialize(UiLength.Capped(0.2, 12)), Is.EqualTo("""{"basis":0.2,"maxOfCell":0.1}"""));
		});
	}

	[Test]
	public void The_caps_travel_beside_the_parent_fraction()
	{
		var length = UiLength.OfParent(0.5, 0.1) with { MaxOfCross = 0.4, MaxOfCell = 0.25 };

		Assert.That(UiCanonicalJson.Serialize(length),
			Is.EqualTo("""{"basis":0.1,"maxOfCross":0.4,"maxOfCell":0.25,"ofParent":0.5}"""));
	}

	[Test]
	public void A_parent_relative_size_reaches_the_tree_of_an_element_that_takes_a_size()
	{
		var tree = Build(new UiGauge { Key = "gauge", Thickness = UiSize.FromParent(0.085, 0.01) });

		var gauge = tree.Root.Children.Single();

		Assert.That(gauge.Properties["thickness"].GetRawText(), Is.EqualTo("""{"basis":0.01,"ofParent":0.085}"""));
	}

	[Test]
	public void A_parent_relative_length_converts_where_a_size_is_expected()
	{
		var tree = Build(new UiGauge { Key = "gauge", Thickness = UiLength.OfParent(0.085, 0.01) });

		Assert.That(tree.Root.Children.Single().Properties["thickness"].GetRawText(),
			Is.EqualTo("""{"basis":0.01,"ofParent":0.085}"""));
	}

	[Test]
	public void A_frame_rejects_a_negative_parent_fraction()
	{
		Assert.Throws<ArgumentOutOfRangeException>(
			() => _ = new UiFrame { MaxWidth = UiLength.OfParent(-0.1, 0.1) });
	}

	[Test]
	public void A_grid_that_chooses_its_columns_sends_the_minimum_beside_the_fallback_arrangement()
	{
		var tree = Build(new UiGrid
		{
			Key = "rings",
			Columns = 4,
			MinCellSize = UiLength.OfBasis(0.25),
			Children = [new UiTextRun { Key = "a", Text = "a" }],
		});

		var grid = tree.Root.Children.Single();

		Assert.Multiple(() =>
		{
			Assert.That(grid.Properties["minCellSize"].GetRawText(), Is.EqualTo("""{"basis":0.25}"""));
			Assert.That(grid.Properties["columns"].GetInt32(), Is.EqualTo(4));
		});
	}

	[Test]
	public void A_grid_without_a_minimum_cell_size_sends_no_such_property()
	{
		var tree = Build(new UiGrid { Key = "rings", Columns = 2, Children = [new UiTextRun { Key = "a", Text = "a" }] });

		Assert.That(tree.Root.Children.Single().Properties.ContainsKey("minCellSize"), Is.False);
	}

	[Test]
	public void The_ring_panel_for_fifteen_devices_carries_each_device_once()
	{
		static long TreeBytes(int devices)
		{
			var rings = Enumerable.Range(0, devices).Select(index => (UiElement)RingFor(index)).ToArray();
			var tree = Build(new UiGrid { Key = "rings", MinCellSize = UiLength.OfBasis(0.25), Columns = 4, Children = rings });

			return UiCanonicalJson.SerializeToUtf8Bytes(tree).Length;
		}

		var two = TreeBytes(2);
		var eight = TreeBytes(8);
		var fifteen = TreeBytes(15);
		var perDevice = (eight - two) / 6d;

		Assert.Multiple(() =>
		{
			Assert.That(fifteen, Is.LessThan(MaxTreeBytes / 4));
			Assert.That(fifteen - eight, Is.EqualTo(perDevice * 7).Within(perDevice * 0.35), "linear in the number of devices");
		});
	}

	private static UiLayer RingFor(int index) => new()
	{
		Key = $"ring{index}",
		Children =
		[
			new UiGauge { Key = $"gauge{index}", Level = 0.5, Thickness = UiLength.OfParent(0.085, 0.01) },
			new UiIcon { Key = $"icon{index}", Icon = "battery", Size = UiLength.OfParent(0.3, 0.05) },
			new UiTextRun { Key = $"percent{index}", Text = "50 %", Size = UiLength.OfParent(0.18, 0.03) },
		],
	};
}
