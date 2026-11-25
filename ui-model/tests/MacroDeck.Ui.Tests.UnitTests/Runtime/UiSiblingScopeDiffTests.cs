using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Testing;

namespace MacroDeck.Ui.Tests.UnitTests.Runtime;

[TestFixture]
public class UiSiblingScopeDiffTests
{
	private static readonly string[] _beforeShown = ["list.before", "list.fixed"];
	private static readonly string[] _afterShown = ["list.fixed", "list.after"];
	private static readonly string[] _rowsAfterSwitch = ["rows.b", "rows.c", "rows.tail"];

	private static UiSurface WidgetSurface()
	{
		return new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared };
	}

	private static IReadOnlyList<UiPatchOperation> SingleFlush(UiTestHost host)
	{
		Assert.That(host.Patches, Has.Count.EqualTo(1));

		return host.Patches[0].Operations;
	}

	private static int IndexOf(IReadOnlyList<UiPatchOperation> operations, string op, string nodeId)
	{
		for (var index = 0; index < operations.Count; index++)
		{
			if (operations[index].Op == op && operations[index].NodeId == nodeId)
			{
				return index;
			}
		}

		return -1;
	}

	[Test]
	public void Switching_back_from_a_layer_whose_fallback_reuses_the_flat_row_id_removes_the_layer_first()
	{
		var layered = new UiState<bool>(false);
		var row = new UiTextRun { Key = "row", Text = "Main" };

		var host = UiTestHost.Render(new UiStack
			{
				Key = "button",
				Children =
				[
					new UiWhen { Key = "flat", Condition = () => !layered.Value, Content = () => row },
					new UiWhen
					{
						Key = "layered",
						Condition = () => layered.Value,
						Content = () => new UiLayer
						{
							Key = "labels",
							Children = [new UiTextRun { Key = "primary", Text = "Main" }],
							Fallback = row
						}
					}
				]
			},
			WidgetSurface());

		layered.Value = true;
		host.ClearPatches();

		layered.Value = false;

		var operations = SingleFlush(host);
		var remove = IndexOf(operations, UiPatchOperations.RemoveNode, "button.labels");
		var insert = IndexOf(operations, UiPatchOperations.InsertNode, "button.row");

		Assert.Multiple(() =>
		{
			Assert.That(remove, Is.Not.Negative, host.DescribePatches());
			Assert.That(insert, Is.GreaterThan(remove), host.DescribePatches());
			Assert.That(host.ById("button.row").Text("text"), Is.EqualTo("Main"));
			Assert.That(host.FindById("button.labels"), Is.Null);
		});
	}

	[Test]
	public void Conditionals_on_both_sides_of_a_fixed_child_flipping_together_insert_at_their_rendered_indices()
	{
		var before = new UiState<bool>(false);
		var after = new UiState<bool>(true);

		var host = UiTestHost.Render(new UiStack
			{
				Key = "list",
				Children =
				[
					new UiWhen
					{
						Key = "beforeGate",
						Condition = () => before.Value,
						Content = () => new UiTextRun { Key = "before", Text = "Before" }
					},
					new UiTextRun { Key = "fixed", Text = "Fixed" },
					new UiWhen
					{
						Key = "afterGate",
						Condition = () => after.Value,
						Content = () => new UiTextRun { Key = "after", Text = "After" }
					}
				]
			},
			WidgetSurface());

		using (host.View.Batch())
		{
			after.Value = false;
			before.Value = true;
		}

		Assert.That(host.Tree.Root.Children.Select(child => child.Id), Is.EqualTo(_beforeShown));

		using (host.View.Batch())
		{
			before.Value = false;
			after.Value = true;
		}

		Assert.That(host.Tree.Root.Children.Select(child => child.Id), Is.EqualTo(_afterShown));
	}

	[Test]
	public void A_node_with_the_same_id_in_both_branches_of_a_conditional_pair_survives_the_switch()
	{
		var second = new UiState<bool>(false);

		var host = UiTestHost.Render(new UiStack
			{
				Key = "pair",
				Children =
				[
					new UiWhen
					{
						Key = "firstBranch",
						Condition = () => !second.Value,
						Content = () => new UiTextRun { Key = "shared", Text = "First" }
					},
					new UiWhen
					{
						Key = "secondBranch",
						Condition = () => second.Value,
						Content = () => new UiTextRun { Key = "shared", Text = "Second" }
					}
				]
			},
			WidgetSurface());

		second.Value = true;

		var operations = SingleFlush(host);

		Assert.Multiple(() =>
		{
			Assert.That(operations.Select(operation => operation.Op),
				Has.None.AnyOf(UiPatchOperations.InsertNode, UiPatchOperations.RemoveNode),
				host.DescribePatches());
			Assert.That(host.ById("pair.shared").Text("text"), Is.EqualTo("Second"));
		});
	}

	[TestCase(true)]
	[TestCase(false)]
	public void A_conditional_inside_a_repeat_flipping_with_the_repeat_list_yields_an_applicable_patch(
		bool conditionFirst)
	{
		IReadOnlyList<string> initial = ["a", "b"];
		var items = new UiState<IReadOnlyList<string>>(initial);
		var shown = new UiState<bool>(false);

		var host = UiTestHost.Render(new UiStack
			{
				Key = "rows",
				Children =
				[
					new UiRepeat<string>
					{
						Key = "items",
						Items = UiValue.From(() => items.Value),
						KeySelector = item => item,
						Template = (item, _) => new UiWhen
						{
							Key = $"gate{item}",
							Condition = () => shown.Value,
							Content = () => new UiTextRun { Key = item, Text = item }
						}
					},
					new UiTextRun { Key = "tail", Text = "Tail" }
				]
			},
			WidgetSurface());

		using (host.View.Batch())
		{
			if (conditionFirst)
			{
				shown.Value = true;
				items.Value = ["b", "c"];
			}
			else
			{
				items.Value = ["b", "c"];
				shown.Value = true;
			}
		}

		Assert.That(host.Tree.Root.Children.Select(child => child.Id),
			Is.EqualTo(_rowsAfterSwitch),
			host.DescribePatches());
	}
}
