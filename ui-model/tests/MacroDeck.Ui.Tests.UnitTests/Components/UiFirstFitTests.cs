using MacroDeck.Ui.Components;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Negotiation;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Ui.Tests.UnitTests.Components;

[TestFixture]
public class UiFirstFitTests
{
	private static readonly string[] _onlyFill = ["fill"];
	private static readonly string[] _drawnLayouts = ["root.group.inline", "root.group.stacked"];

	private static UiTree Build(params UiElement[] children)
		=> UiViewBuilder.Build(
			new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
			new UiStack { Key = "root", Children = children });

	private static IEnumerable<UiNode> Walk(UiNode node)
	{
		yield return node;

		foreach (var descendant in node.Children.Concat(node.Fallback is null ? [] : [node.Fallback]).SelectMany(Walk))
		{
			yield return descendant;
		}
	}

	private static UiFirstFit Group(UiElement? fallback = null) => new()
	{
		Key = "group",
		Fill = true,
		Fallback = fallback,
		Children =
		[
			new UiStack
			{
				Key = "inline",
				Direction = UiComponentDirections.Horizontal,
				Children = [new UiTextRun { Key = "name", Text = "Mouse" }, new UiTextRun { Key = "caption", Text = "0:35 to full" }],
			},
			new UiStack
			{
				Key = "stacked",
				Children = [new UiTextRun { Key = "name", Text = "Mouse" }, new UiTextRun { Key = "caption", Text = "0:35 to full" }],
			},
		],
	};

	[Test]
	public void The_node_carries_its_layouts_in_order_and_no_properties_of_its_own()
	{
		var node = Build(Group()).Root.Children.Single();

		Assert.Multiple(() =>
		{
			Assert.That(node.Type, Is.EqualTo("ui.first-fit"));
			Assert.That(node.Children.Select(child => child.Id), Is.EqualTo(_drawnLayouts));
			Assert.That(node.Properties.Keys, Is.EquivalentTo(_onlyFill));
		});
	}

	[Test]
	public void Without_an_explicit_fallback_the_last_layout_is_copied_as_the_fallback_under_its_own_ids()
	{
		var node = Build(Group()).Root.Children.Single();

		Assert.Multiple(() =>
		{
			Assert.That(node.Fallback!.Id, Is.EqualTo("root.group._fallback.stacked"));
			Assert.That(node.Fallback.Type, Is.EqualTo("ui.stack"));
			Assert.That(Walk(node).Select(n => n.Id), Is.Unique);
		});
	}

	[Test]
	public void An_explicit_fallback_replaces_the_copy()
	{
		var node = Build(Group(new UiTextRun { Key = "plain", Text = "Mouse" })).Root.Children.Single();

		Assert.Multiple(() =>
		{
			Assert.That(node.Fallback!.Id, Is.EqualTo("root.plain"));
			Assert.That(Walk(node).Any(n => n.Id.Contains("_fallback", StringComparison.Ordinal)), Is.False);
		});
	}

	[Test]
	public void A_reader_without_ui_first_fit_draws_the_last_layout_through_the_fallback()
	{
		var node = Build(Group()).Root.Children.Single();
		var older = new UiCapabilities
		{
			UiProtocol = new UiVersionRange { Minimum = 3, Maximum = 4 },
			SupportsAllComponents = false,
			Components = new Dictionary<string, UiVersionRange>
			{
				["ui.stack"] = new() { Minimum = 1, Maximum = 1 },
				["ui.text"] = new() { Minimum = 1, Maximum = 1 },
			},
		};

		Assert.Multiple(() =>
		{
			Assert.That(UiCapabilityNegotiator.NegotiateComponent(node, older).IsSupported, Is.False);
			Assert.That(UiCapabilityNegotiator.NegotiateComponent(node.Fallback!, older).IsSupported, Is.True);
			Assert.That(node.Fallback!.Properties.GetValueOrDefault("direction").ToString(), Does.Not.Contain("horizontal"));
		});
	}

	[Test]
	public void Layouts_that_cannot_be_drawn_across_the_whole_box_are_rejected()
	{
		Assert.Multiple(() =>
		{
			Assert.Throws<UiViewException>(() => Build(new UiFirstFit
			{
				Key = "f",
				Children = [new UiWhen { Key = "w", Condition = () => true, Content = () => new UiStack { Key = "d" } }],
			}), "conditional layout");
			Assert.Throws<UiViewException>(() => Build(new UiFirstFit
			{
				Key = "f",
				Children = [new UiStack { Key = "_fallback" }],
			}), "reserved key");
			Assert.Throws<UiViewException>(() => Build(new UiFirstFit
			{
				Key = "f",
				Children = [new UiStack { Key = "a" }, new UiStack { Key = "b", Fill = true }],
			}), "slot property on a layout");
		});
	}

	[Test]
	public void An_input_in_the_last_layout_that_is_copied_is_rejected_naming_the_explicit_fallback_way_out()
	{
		var text = new UiState<string>("");
		UiFirstFit Form(UiElement? fallback) => new()
		{
			Key = "form",
			Fallback = fallback,
			Children = [new UiStack { Key = "fields", Children = [new UiStringInput { Key = "name", Binding = Bind.To(text) }] }],
		};

		var exception = Assert.Throws<UiViewException>(() => Build(Form(null)));

		Assert.Multiple(() =>
		{
			Assert.That(exception!.Message, Does.Contain("'form'").And.Contain("explicit Fallback"));
			Assert.DoesNotThrow(() => Build(Form(new UiStack { Key = "plain" })));
		});
	}

	[Test]
	public void A_node_with_no_layouts_builds_and_has_no_fallback()
	{
		var node = Build(new UiFirstFit { Key = "empty" }).Root.Children.Single();

		Assert.Multiple(() =>
		{
			Assert.That(node.Children, Is.Empty);
			Assert.That(node.Fallback, Is.Null);
		});
	}
}
