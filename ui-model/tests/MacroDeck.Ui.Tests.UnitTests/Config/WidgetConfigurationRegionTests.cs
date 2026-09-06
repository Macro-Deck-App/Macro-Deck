using MacroDeck.Ui.Config;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Ui.Tests.UnitTests.Config;

/// <summary>
/// The widget configuration contract from a renderer's point of view: what it receives is a root carrying
/// one or two named regions, and it decides where each is drawn. A renderer that could not tell the property
/// list from the specialized editor would have to guess a layout from child order, which is the thing naming
/// the regions exists to avoid.
/// </summary>
[TestFixture]
public class WidgetConfigurationRegionTests
{
	private static readonly string[] _bothRegions = ["widget-properties", "widget-editor"];
	private static readonly string[] _propertiesOnly = ["widget-properties"];
	private static readonly string[] _bareInputIds = ["label", "flows"];

	private static UiSurface ConfigSurface()
		=> new() { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive };

	[Test]
	public void A_configuration_serves_its_two_regions_as_the_roots_children_in_order()
	{
		var tree = UiViewBuilder.Build(ConfigSurface(),
			new UiWidgetConfiguration
			{
				Key = "root",
				Properties = new UiWidgetProperties
				{
					Key = "properties",
					Children = [new UiStringInput { Key = "label" }],
				},
				Editor = new UiWidgetEditor
				{
					Key = "editor",
					Children = [new UiActionsListEditor { Key = "flows" }],
				},
			});

		Assert.Multiple(() =>
		{
			Assert.That(tree.Root.Type, Is.EqualTo("widget-configuration"));
			Assert.That(tree.Root.Children.Select(child => child.Type),
				Is.EqualTo(_bothRegions).AsCollection);
		});
	}

	[Test]
	public void A_configuration_that_declares_no_editor_serves_only_the_property_region()
	{
		var tree = UiViewBuilder.Build(ConfigSurface(),
			new UiWidgetConfiguration
			{
				Key = "root",
				Properties = new UiWidgetProperties
				{
					Key = "properties",
					Children = [new UiStringInput { Key = "label" }],
				},
			});

		Assert.Multiple(() =>
		{
			Assert.That(tree.Root.Children.Select(child => child.Type),
				Is.EqualTo(_propertiesOnly).AsCollection,
				"the editor region is optional, and an absent one is absent rather than empty");
			Assert.That(tree.Root.Children, Has.Count.EqualTo(1));
		});
	}

	/// <summary>
	/// The two regions are named properties rather than positional children, so an author may write them in
	/// either order. Both orders have to reach the same tree, or the contract would quietly depend on the
	/// order of an object initializer.
	/// </summary>
	[Test]
	public void Declaring_the_editor_before_the_properties_serves_the_same_regions()
	{
		var editorFirst = UiViewBuilder.Build(ConfigSurface(),
			new UiWidgetConfiguration
			{
				Key = "root",
				Editor = new UiWidgetEditor { Key = "editor" },
				Properties = new UiWidgetProperties { Key = "properties" },
			});

		Assert.That(editorFirst.Root.Children.Select(child => child.Type),
			Is.EqualTo(_bothRegions).AsCollection);
	}

	/// <summary>
	/// A top-level input's id is the widget data key it configures, in both regions, because a region opens
	/// no input-id scope. An implementation that scoped ids by region would produce
	/// <c>properties.label</c> and write a key no widget schema has.
	/// </summary>
	[Test]
	public void A_top_level_input_in_either_region_is_addressed_by_its_bare_key()
	{
		var tree = UiViewBuilder.Build(ConfigSurface(),
			new UiWidgetConfiguration
			{
				Key = "root",
				Properties = new UiWidgetProperties
				{
					Key = "properties",
					Children = [new UiStringInput { Key = "label" }],
				},
				Editor = new UiWidgetEditor
				{
					Key = "editor",
					Children = [new UiActionsListEditor { Key = "flows" }],
				},
			});

		var inputIds = Walk(tree.Root)
			.Where(node => node.Type is "string" or "actions-list-editor")
			.Select(node => node.Id)
			.ToList();

		Assert.That(inputIds, Is.EquivalentTo(_bareInputIds));
	}

	private static IEnumerable<UiNode> Walk(UiNode node)
	{
		yield return node;

		foreach (var child in node.Children)
		{
			foreach (var descendant in Walk(child))
			{
				yield return descendant;
			}
		}
	}
}
