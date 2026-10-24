using System.Text.Json;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Ui.Tests.UnitTests.Config;

[TestFixture]
public class UiPageLayoutTests
{
	private static readonly string[] _regionTypes = ["stack", "stack", "stack"];
	private static readonly string[] _regionIds = ["page.rail-actions", "page.rail", "page.detail"];
	private static readonly string[] _activateOnly = ["activate"];
	private static readonly string[] _plainStackKeys = ["direction", "accent"];
	private static readonly string[] _plainHeadingKeys = ["text"];
	private static readonly string[] _plainButtonKeys = ["label", "icon"];

	private static UiSurface PageSurface()
	{
		return new UiSurface { Kind = UiSurfaceKinds.Page, SessionMode = UiSessionModes.Exclusive };
	}

	private static IEnumerable<UiNode> Walk(UiNode node)
	{
		yield return node;

		foreach (var child in node.Children.SelectMany(Walk))
		{
			yield return child;
		}
	}

	[Test]
	public void A_split_carries_its_three_regions_as_stacks_in_a_fixed_order()
	{
		using var view = new UiView(PageSurface(),
			new UiSplit
			{
				Key = "page",
				RailTitle = "Scripts",
				RailActions = [new UiConfigButton { Key = "new", Label = "New", Icon = "plus" }],
				Rail = [new UiConfigList { Key = "scripts" }],
				Detail = [new UiProse { Key = "hint", Text = "Pick a script." }]
			});

		var split = view.Tree.Root;

		Assert.Multiple(() =>
		{
			Assert.That(split.Type, Is.EqualTo("split"));
			Assert.That(split.Properties["railTitle"].GetString(), Is.EqualTo("Scripts"));
			Assert.That(split.Children.Select(region => region.Type), Is.EqualTo(_regionTypes));
			Assert.That(split.Children.Select(region => region.Id),
				Is.EqualTo(_regionIds));
			Assert.That(split.Children.Select(region => region.Properties.Count), Is.All.EqualTo(0));
			Assert.That(split.Children[0].Children.Single().Type, Is.EqualTo("button"));
			Assert.That(split.Children[1].Children.Single().Type, Is.EqualTo("list"));
			Assert.That(split.Children[2].Children.Single().Type, Is.EqualTo("prose"));
		});
	}

	[Test]
	public void An_empty_region_is_still_present_so_the_positions_never_shift()
	{
		using var view = new UiView(PageSurface(),
			new UiSplit { Key = "page", Detail = [new UiDivider { Key = "rule" }] });

		var regions = view.Tree.Root.Children;

		Assert.Multiple(() =>
		{
			Assert.That(regions, Has.Count.EqualTo(3));
			Assert.That(regions[0].Children, Is.Empty);
			Assert.That(regions[1].Children, Is.Empty);
			Assert.That(regions[2].Children.Single().Type, Is.EqualTo("divider"));
		});
	}

	[Test]
	public void Inputs_in_the_detail_keep_their_bare_keys_and_no_id_repeats()
	{
		var name = new UiState<string>("Hello");
		using var view = new UiView(PageSurface(),
			new UiSplit
			{
				Key = "page",
				Rail =
				[
					new UiConfigList
					{
						Key = "scripts",
						Children = [new UiConfigListItem { Key = "detail", Label = "A row keyed like a region" }]
					}
				],
				Detail =
				[
					new UiConfigStack
					{
						Key = "card",
						Variant = "card",
						Children =
						[
							new UiStringInput { Key = "name", Binding = Bind.To(name) },
							new UiCodeInput { Key = "source", Language = UiCodeLanguages.AutoHotkey }
						]
					}
				]
			});

		var ids = Walk(view.Tree.Root).Select(node => node.Id).ToList();
		var result = view.Dispatch(new UiEvent
		{
			NodeId = "name", Name = UiConfigEvents.Change, Data = UiCanonicalJson.ToElement("World")
		});

		Assert.Multiple(() =>
		{
			Assert.That(ids, Is.Unique);
			Assert.That(ids, Does.Contain("name"));
			Assert.That(ids, Does.Contain("source"));
			Assert.That(result.IsAccepted, Is.True);
			Assert.That(name.Value, Is.EqualTo("World"));
		});
	}

	[Test]
	public void A_list_item_carries_its_row_properties_its_activate_event_and_a_trailing_menu()
	{
		var activated = false;
		using var view = new UiView(PageSurface(),
			new UiConfigList
			{
				Key = "scripts",
				Label = "Scripts",
				Children =
				[
					new UiConfigListItem
					{
						Key = "first",
						Label = "First",
						Description = "Exited with 0",
						Icon = "file",
						Badge = "2",
						Severity = "success",
						Selected = true,
						Events = [UiEventHandler.On(UiConfigEvents.Activate, () => activated = true)],
						Children =
						[
							new UiConfigMenu
							{
								Key = "actions",
								Label = "Actions",
								Children = [new UiConfigButton { Key = "delete", Label = "Delete", Variant = "danger" }]
							}
						]
					}
				]
			});

		var item = view.Tree.Root.Children.Single();
		var result = view.Dispatch(new UiEvent { NodeId = item.Id, Name = "activate" });

		Assert.Multiple(() =>
		{
			Assert.That(view.Tree.Root.Type, Is.EqualTo("list"));
			Assert.That(item.Type, Is.EqualTo("list-item"));
			Assert.That(item.Properties["label"].GetString(), Is.EqualTo("First"));
			Assert.That(item.Properties["description"].GetString(), Is.EqualTo("Exited with 0"));
			Assert.That(item.Properties["icon"].GetString(), Is.EqualTo("file"));
			Assert.That(item.Properties["badge"].GetString(), Is.EqualTo("2"));
			Assert.That(item.Properties["severity"].GetString(), Is.EqualTo("success"));
			Assert.That(item.Properties["selected"].GetBoolean(), Is.True);
			Assert.That(item.Properties["events"].EnumerateArray().Select(name => name.GetString()),
				Is.EqualTo(_activateOnly));
			Assert.That(item.Children.Single().Type, Is.EqualTo("menu"));
			Assert.That(result.IsAccepted, Is.True);
			Assert.That(activated, Is.True);
		});
	}

	[Test]
	public void An_empty_state_carries_its_icon_title_and_text()
	{
		using var view = new UiView(PageSurface(),
			new UiEmptyState { Key = "nothing", Icon = "file", Title = "No script", Text = "Pick one." });

		var properties = view.Tree.Root.Properties;

		Assert.Multiple(() =>
		{
			Assert.That(view.Tree.Root.Type, Is.EqualTo("empty-state"));
			Assert.That(properties["icon"].GetString(), Is.EqualTo("file"));
			Assert.That(properties["title"].GetString(), Is.EqualTo("No script"));
			Assert.That(properties["text"].GetString(), Is.EqualTo("Pick one."));
		});
	}

	[Test]
	public void Stacks_buttons_and_headings_carry_the_new_presentation_keys()
	{
		using var view = new UiView(PageSurface(),
			new UiConfigStack
			{
				Key = "footer",
				Align = "end",
				Gap = "large",
				Variant = "card",
				Children =
				[
					new UiHeading { Key = "title", Text = "Script", Badge = "Unsaved" },
					new UiConfigButton
					{
						Key = "save", Label = "Save", Icon = "check", Variant = "primary", IconWithLabel = true
					}
				]
			});

		var stack = view.Tree.Root;
		var heading = stack.Children[0];
		var button = stack.Children[1];

		Assert.Multiple(() =>
		{
			Assert.That(stack.Properties["align"].GetString(), Is.EqualTo("end"));
			Assert.That(stack.Properties["gap"].GetString(), Is.EqualTo("large"));
			Assert.That(stack.Properties["variant"].GetString(), Is.EqualTo("card"));
			Assert.That(heading.Properties["badge"].GetString(), Is.EqualTo("Unsaved"));
			Assert.That(button.Properties["variant"].GetString(), Is.EqualTo("primary"));
			Assert.That(button.Properties["iconWithLabel"].GetBoolean(), Is.True);
		});
	}

	[Test]
	public void A_tree_written_without_the_new_properties_carries_exactly_the_keys_it_did_before()
	{
		using var view = new UiView(PageSurface(),
			new UiConfigStack
			{
				Key = "row",
				Direction = "horizontal",
				Accent = "#5865f2",
				Children =
				[
					new UiHeading { Key = "title", Text = "Script" },
					new UiConfigButton { Key = "save", Label = "Save", Icon = "check" }
				]
			});

		var stack = view.Tree.Root;

		Assert.Multiple(() =>
		{
			Assert.That(stack.Properties.Keys, Is.EquivalentTo(_plainStackKeys));
			Assert.That(stack.Children[0].Properties.Keys, Is.EquivalentTo(_plainHeadingKeys));
			Assert.That(stack.Children[1].Properties.Keys, Is.EquivalentTo(_plainButtonKeys));
		});
	}

	[Test]
	public void A_new_layout_type_carries_only_the_fallback_its_author_gave_it()
	{
		using var view = new UiView(PageSurface(),
			new UiConfigStack
			{
				Key = "root",
				Children =
				[
					new UiSplit { Key = "bare" },
					new UiSplit
					{
						Key = "page",
						Fallback = new UiProse { Key = "update", Text = "Update Macro Deck to see this page." }
					}
				]
			});

		var children = view.Tree.Root.Children;

		Assert.Multiple(() =>
		{
			Assert.That(children[0].Fallback, Is.Null);
			Assert.That(children[1].Fallback!.Type, Is.EqualTo("prose"));
			Assert.That(JsonSerializer.Serialize(children[1].Fallback!.Properties["text"]),
				Is.EqualTo("\"Update Macro Deck to see this page.\""));
		});
	}
}
