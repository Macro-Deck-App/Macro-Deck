using MacroDeck.Ui.Components;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Negotiation;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Ui.Tests.UnitTests.Components;

[TestFixture]
public class UiModifierTests
{
	private static readonly string[] _handlerOrder = ["child", "inner", "outer", """drag {"x":0.1,"y":-0.2}"""];

	private static UiSurface WidgetSurface()
		=> new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared };

	private static UiTree Build(params UiElement[] children)
		=> UiViewBuilder.Build(WidgetSurface(), new UiStack { Key = "root", Children = children });

	private static UiView View(params UiElement[] children)
		=> new(WidgetSurface(), new UiStack { Key = "root", Children = children });

	private static UiNode? Find(UiNode node, string id)
	{
		if (node.Id == id)
		{
			return node;
		}

		foreach (var child in node.Children)
		{
			if (Find(child, id) is { } found)
			{
				return found;
			}
		}

		return node.Fallback is null ? null : Find(node.Fallback, id);
	}

	private static IEnumerable<UiNode> Walk(UiNode node)
	{
		yield return node;

		foreach (var descendant in node.Children.SelectMany(Walk))
		{
			yield return descendant;
		}

		if (node.Fallback is not null)
		{
			foreach (var descendant in Walk(node.Fallback))
			{
				yield return descendant;
			}
		}
	}

	private static UiDispatchResult Press(UiView view, string nodeId)
		=> view.Dispatch(new UiEvent { NodeId = nodeId, Name = UiComponentEvents.Press });

	private static UiButton PressableButton(string key, Action onPress)
		=> new() { Key = key, Events = [UiEventHandler.On(UiComponentEvents.Press, onPress)] };

	[Test]
	public void Node_modifiers_merge_onto_the_child_which_keeps_its_id_and_no_node_of_their_own_is_emitted()
	{
		var tree = Build(new UiModifier
		{
			Key = "decoration",
			Background = "#112233",
			Radius = 0.1,
			Child = new UiStack { Key = "card" },
		});

		var card = tree.Root.Children.Single();

		Assert.Multiple(() =>
		{
			Assert.That(card.Id, Is.EqualTo("root.card"));
			Assert.That(card.Type, Is.EqualTo("ui.stack"));
			Assert.That(card.Properties["modifiers"].GetRawText(),
				Is.EqualTo("""{"background":"#112233","radius":{"basis":0.1}}"""));
			Assert.That(Walk(tree.Root).Any(node => node.Type == "ui.modifier" || node.Id.Contains("decoration")),
				Is.False);
		});
	}

	[Test]
	public void Nested_node_modifiers_merge_outward_onto_one_node()
	{
		var tree = Build(new UiModifier
		{
			Key = "outer",
			BorderColor = "#ff0000",
			BorderLine = UiComponentBorderLines.Dashed,
			Child = new UiModifier
			{
				Key = "inner",
				BorderWidth = 0.02,
				AccessibilityLabel = "Volume",
				Child = new UiTextRun { Key = "label", Text = "42" },
			},
		});

		Assert.That(tree.Root.Children.Single().Properties["modifiers"].GetRawText(),
			Is.EqualTo("""{"accessibilityLabel":"Volume","borderColor":"#ff0000","borderLine":"dashed","borderWidth":{"basis":0.02}}"""));
	}

	[Test]
	public void Assigning_one_member_twice_on_one_node_is_rejected()
	{
		Assert.Throws<UiViewException>(() => Build(new UiModifier
		{
			Key = "outer",
			Background = "#000000",
			Child = new UiModifier { Key = "inner", Background = "#ffffff", Child = new UiStack { Key = "card" } },
		}));
	}

	[Test]
	public void A_modifier_whose_child_can_produce_other_than_one_node_is_rejected()
	{
		Assert.Multiple(() =>
		{
			Assert.Throws<UiViewException>(() => Build(new UiModifier
			{
				Key = "m",
				Radius = 0.1,
				Child = new UiWhen { Key = "w", Condition = () => true, Content = () => new UiStack { Key = "s" } },
			}));
			Assert.Throws<UiViewException>(() => Build(new UiModifier
			{
				Key = "m",
				Padding = 0.1,
				Child = new UiFragment { Key = "f", Children = [new UiStack { Key = "a" }, new UiStack { Key = "b" }] },
			}));
		});
	}

	[Test]
	public void A_wrapper_member_emits_a_modifier_node_carrying_the_wrapper_properties_and_its_own_modifiers()
	{
		var tree = Build(new UiModifier
		{
			Key = "frame",
			Padding = 0.05,
			Opacity = 0.5,
			Clip = UiComponentClips.Circle,
			Mask = UiMask.Linear(90,
				new UiMaskStop { Offset = 0, Opacity = 1 },
				new UiMaskStop { Offset = 1, Opacity = 0 }),
			Frame = new UiFrame { Width = 0.5, AspectRatio = 1 },
			Background = UiGradient.Radial(0.5, 0.5,
				new UiGradientStop { Offset = 0, Color = "#ffffff" },
				new UiGradientStop { Offset = 1, Color = "#000000" }),
			Child = new UiTextRun { Key = "label", Text = "On" },
		});

		var wrapper = tree.Root.Children.Single();

		Assert.Multiple(() =>
		{
			Assert.That(wrapper.Id, Is.EqualTo("root.frame"));
			Assert.That(wrapper.Type, Is.EqualTo("ui.modifier"));
			Assert.That(wrapper.Children.Single().Id, Is.EqualTo("root.frame.label"));
			Assert.That(wrapper.Properties["padding"].GetRawText(), Is.EqualTo("""{"basis":0.05}"""));
			Assert.That(wrapper.Properties["opacity"].GetRawText(), Is.EqualTo("0.5"));
			Assert.That(wrapper.Properties["clip"].GetString(), Is.EqualTo("circle"));
			Assert.That(wrapper.Properties["mask"].GetRawText(),
				Is.EqualTo("""{"linear":{"angle":90,"stops":[{"offset":0,"opacity":1},{"offset":1,"opacity":0}]}}"""));
			Assert.That(wrapper.Properties["frame"].GetRawText(),
				Is.EqualTo("""{"width":{"basis":0.5},"aspectRatio":1}"""));
			Assert.That(wrapper.Properties["modifiers"].GetRawText(),
				Is.EqualTo("""{"background":{"radial":{"centerX":0.5,"centerY":0.5,"stops":[{"offset":0,"color":"#ffffff"},{"offset":1,"color":"#000000"}]}}}"""));
			Assert.That(wrapper.Children.Single().Properties.ContainsKey("modifiers"), Is.False);
		});
	}

	[Test]
	public void A_wrapper_carries_only_the_fallback_it_was_given_and_nested_wrappers_do_not_multiply()
	{
		var tree = Build(
			new UiModifier
			{
				Key = "given",
				Padding = 0.05,
				Fallback = new UiStack { Key = "plain", Children = [new UiTextRun { Key = "plainLabel", Text = "On" }] },
				Child = new UiTextRun { Key = "label", Text = "On" },
			},
			new UiModifier
			{
				Key = "outer",
				Padding = 0.05,
				Child = new UiModifier { Key = "inner", Opacity = 0.5, Child = new UiTextRun { Key = "text", Text = "x" } },
			});

		var given = tree.Root.Children[0];
		var outer = tree.Root.Children[1];

		Assert.Multiple(() =>
		{
			Assert.That(given.Fallback!.Id, Is.EqualTo("root.plain"));
			Assert.That(given.Fallback.Type, Is.EqualTo("ui.stack"));
			Assert.That(given.Fallback.Children.Single().Id, Is.EqualTo("root.plain.plainLabel"));

			Assert.That(outer.Fallback, Is.Null, "no fallback is invented when none was given");
			Assert.That(outer.Children.Single().Type, Is.EqualTo("ui.modifier"));
			Assert.That(outer.Children.Single().Fallback, Is.Null);
			Assert.That(Walk(outer).Count(node => node.Type == "ui.modifier"), Is.EqualTo(2));
			Assert.That(Walk(outer).Count(), Is.EqualTo(3));
		});
	}

	[Test]
	public void A_wrapped_child_that_sizes_itself_in_the_parent_is_rejected()
	{
		Assert.Multiple(() =>
		{
			Assert.Throws<UiViewException>(() => Build(new UiModifier
			{
				Key = "m",
				Padding = 0.1,
				Child = new UiTextRun { Key = "t", MainSize = 0.2 },
			}));
			Assert.Throws<UiViewException>(() => Build(new UiModifier
			{
				Key = "m",
				Frame = new UiFrame { AspectRatio = 1 },
				Child = new UiModifier { Key = "safe", Radius = 0.1, Child = new UiStack { Key = "s", Fill = true } },
			}));
		});
	}

	[Test]
	public void A_reader_without_ui_modifier_negotiates_the_wrapper_to_its_fallback()
	{
		var tree = Build(new UiModifier
		{
			Key = "frame",
			Padding = 0.05,
			Fallback = new UiTextRun { Key = "plain", Text = "On" },
			Child = new UiTextRun { Key = "label", Text = "On" },
		});

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

		var wrapper = tree.Root.Children.Single();

		Assert.Multiple(() =>
		{
			Assert.That(UiCapabilityNegotiator.NegotiateComponent(wrapper, older).IsSupported, Is.False);
			Assert.That(UiCapabilityNegotiator.NegotiateComponent(wrapper.Fallback!, older).IsSupported, Is.True);
		});
	}

	[Test]
	public void Merged_gesture_handlers_are_declared_and_run_child_first_then_modifiers_inside_out()
	{
		var calls = new List<string>();
		var view = View(new UiModifier
		{
			Key = "outer",
			Radius = 0.1,
			Events = [UiEventHandler.On(UiComponentEvents.Press, () => calls.Add("outer"))],
			Child = new UiModifier
			{
				Key = "inner",
				BorderWidth = 0.01,
				Events =
				[
					UiEventHandler.On(UiComponentEvents.Press, () => calls.Add("inner")),
					UiEventHandler.On(UiComponentEvents.Drag, data => calls.Add("drag " + data.Raw!.Value.GetRawText())),
				],
				Child = PressableButton("button", () => calls.Add("child")),
			},
		});

		var button = view.Tree.Root.Children.Single();
		var press = Press(view, "root.button");
		var drag = view.Dispatch(new UiEvent
		{
			NodeId = "root.button",
			Name = UiComponentEvents.Drag,
			Data = UiCanonicalJson.ToElement(new Dictionary<string, double> { ["x"] = 0.1, ["y"] = -0.2 }),
		});

		Assert.Multiple(() =>
		{
			Assert.That(button.Properties["events"].GetRawText(), Is.EqualTo("""["press","drag"]"""));
			Assert.That(press.IsAccepted, Is.True, press.Reason);
			Assert.That(drag.IsAccepted, Is.True, drag.Reason);
			Assert.That(calls, Is.EqualTo(_handlerOrder));
		});
	}

	[Test]
	public void A_disabled_wrapper_declares_no_events_anywhere_inside_it_including_its_fallback()
	{
		var tree = Build(new UiModifier
		{
			Key = "frame",
			Padding = 0.05,
			Disabled = true,
			Events = [UiEventHandler.On(UiComponentEvents.Swipe, () => { })],
			Fallback = PressableButton("fallback", () => { }),
			Child = new UiStack { Key = "row", Children = [PressableButton("button", () => { })] },
		});

		var wrapper = tree.Root.Children.Single();

		Assert.Multiple(() =>
		{
			Assert.That(Walk(wrapper).Where(node => node.Properties.ContainsKey("events")), Is.Empty);
			Assert.That(wrapper.Properties["modifiers"].GetRawText(), Is.EqualTo("""{"disabled":true}"""));
			Assert.That(Find(wrapper, "root.frame.row.button")!.Properties.ContainsKey("modifiers"),
				Is.False,
				"only the node the modifier sits on carries disabled; the rest inherit it");
		});
	}

	[Test]
	public void A_bound_disabled_value_withdraws_and_restores_the_events_of_the_node_it_sits_on()
	{
		var disabled = new UiState<bool>(true);
		var view = View(new UiModifier
		{
			Key = "m",
			Disabled = UiValue.From(() => disabled.Value),
			Child = PressableButton("button", () => { }),
		});

		var before = Find(view.Tree.Root, "root.button")!;
		disabled.Set(false);
		var after = Find(view.Tree.Root, "root.button")!;

		Assert.Multiple(() =>
		{
			Assert.That(before.Properties.ContainsKey("events"), Is.False);
			Assert.That(before.Properties["modifiers"].GetRawText(), Is.EqualTo("""{"disabled":true}"""));
			Assert.That(after.Properties["events"].GetRawText(), Is.EqualTo("""["press"]"""));
			Assert.That(after.Properties.ContainsKey("modifiers"), Is.False, "disabled is written only while true");
		});
	}

	[Test]
	public void A_bound_disabled_ancestor_withdraws_the_events_of_every_node_inside_it()
	{
		var disabled = new UiState<bool>(false);
		var view = View(new UiModifier
		{
			Key = "m",
			Disabled = UiValue.From(() => disabled.Value),
			Child = new UiStack { Key = "row", Children = [PressableButton("button", () => { })] },
		});

		var before = Find(view.Tree.Root, "root.row.button")!;
		disabled.Set(true);
		var after = Find(view.Tree.Root, "root.row.button")!;

		Assert.Multiple(() =>
		{
			Assert.That(before.Properties["events"].GetRawText(), Is.EqualTo("""["press"]"""));
			Assert.That(after.Properties.ContainsKey("events"), Is.False);
		});
	}

	[Test]
	public void Content_a_conditional_inserts_later_inside_a_disabled_region_is_disabled_too()
	{
		var shown = new UiState<bool>(false);
		var view = View(new UiModifier
		{
			Key = "m",
			Disabled = true,
			Child = new UiStack
			{
				Key = "row",
				Children =
				[
					new UiWhen { Key = "maybe", Condition = () => shown.Value, Content = () => PressableButton("button", () => { }) },
				],
			},
		});

		shown.Set(true);
		var inserted = Find(view.Tree.Root, "root.row.button");

		Assert.Multiple(() =>
		{
			Assert.That(inserted, Is.Not.Null);
			Assert.That(inserted!.Properties.ContainsKey("events"), Is.False);
			Assert.That(Press(view, "root.row.button").Outcome, Is.EqualTo(UiDispatchOutcome.Ignored));
		});
	}

	[Test]
	public void A_toggle_wrapped_in_a_disabled_modifier_declares_no_events_and_ignores_a_change()
	{
		var flipped = 0;
		var view = View(new UiModifier
		{
			Key = "frame",
			Padding = 0.05,
			Disabled = true,
			Child = new UiToggle
			{
				Key = "toggle",
				On = true,
				Events = [UiEventHandler.On(UiComponentEvents.Change, () => flipped++)],
			},
		});

		var toggle = Find(view.Tree.Root, "root.frame.toggle")!;
		var result = view.Dispatch(new UiEvent
		{
			NodeId = "root.frame.toggle",
			Name = UiComponentEvents.Change,
			Data = UiCanonicalJson.ToElement(false),
		});

		Assert.Multiple(() =>
		{
			Assert.That(toggle.Type, Is.EqualTo("ui.toggle"));
			Assert.That(toggle.Properties.ContainsKey("events"), Is.False);
			Assert.That(result.Outcome, Is.EqualTo(UiDispatchOutcome.Ignored));
			Assert.That(flipped, Is.Zero);
		});
	}

	[Test]
	public void An_event_aimed_inside_a_disabled_region_is_ignored_and_runs_nothing()
	{
		var pressed = 0;
		var view = View(new UiModifier
		{
			Key = "m",
			Disabled = true,
			Child = new UiStack { Key = "row", Children = [PressableButton("button", () => pressed++)] },
		});

		var result = Press(view, "root.row.button");

		Assert.Multiple(() =>
		{
			Assert.That(result.Outcome, Is.EqualTo(UiDispatchOutcome.Ignored));
			Assert.That(pressed, Is.Zero);
		});
	}

	[Test]
	public void A_bound_input_inside_a_disabled_region_ignores_a_change_and_keeps_its_value()
	{
		var value = new UiState<string>("initial");
		var view = View(new UiModifier
		{
			Key = "m",
			Disabled = true,
			Child = new UiStack
			{
				Key = "row",
				Children = [new UiStringInput { Key = "apiKey", Binding = Bind.To(value) }],
			},
		});

		var result = view.Dispatch(new UiEvent
		{
			NodeId = "apiKey",
			Name = UiComponentEvents.Change,
			Data = UiCanonicalJson.ToElement("written"),
		});

		Assert.Multiple(() =>
		{
			Assert.That(result.Outcome, Is.EqualTo(UiDispatchOutcome.Ignored));
			Assert.That(value.Peek(), Is.EqualTo("initial"));
		});
	}

	[Test]
	public void Out_of_range_frame_and_mask_values_are_rejected_when_they_are_built()
	{
		Assert.Multiple(() =>
		{
			Assert.Throws<ArgumentOutOfRangeException>(() => _ = new UiFrame { AspectRatio = 0 });
			Assert.Throws<ArgumentOutOfRangeException>(() => _ = new UiFrame { AspectRatio = -1 });
			Assert.Throws<ArgumentOutOfRangeException>(() => _ = new UiFrame { Width = -0.1 });
			Assert.Throws<ArgumentOutOfRangeException>(() => _ = new UiFrame { MaxHeight = UiLength.OfBasis(0.5, -1) });
			Assert.Throws<ArgumentOutOfRangeException>(() => _ = new UiMaskStop { Offset = 1.5, Opacity = 1 });
			Assert.Throws<ArgumentOutOfRangeException>(() => _ = new UiMaskStop { Offset = 0, Opacity = -0.1 });
			Assert.Throws<ArgumentOutOfRangeException>(() => _ = new UiGradientStop { Offset = double.NaN, Color = "#000000" });
			Assert.Throws<ArgumentOutOfRangeException>(() =>
				UiMask.Radial(1.2, 0.5, new UiMaskStop { Offset = 0, Opacity = 1 }));
			Assert.DoesNotThrow(() => _ = new UiFrame { Width = 0, MinHeight = 0.2, AspectRatio = 1.5 });
		});
	}

	[Test]
	public void A_gradient_or_mask_with_fewer_than_two_stops_or_a_stop_colour_not_written_rrggbb_is_rejected()
	{
		Assert.Multiple(() =>
		{
			Assert.Throws<ArgumentException>(() =>
				UiGradient.Linear(90, new UiGradientStop { Offset = 0, Color = "#000000" }));
			Assert.Throws<ArgumentException>(() =>
				UiMask.Radial(0.5, 0.5, new UiMaskStop { Offset = 0, Opacity = 1 }));
			Assert.Throws<ArgumentException>(() => _ = new UiGradientStop { Offset = 0, Color = "red" });
			Assert.Throws<ArgumentException>(() => _ = new UiGradientStop { Offset = 0, Color = "#fff" });
			Assert.Throws<ArgumentException>(() => UiBackground.Solid("red"));
			Assert.Throws<ArgumentException>(() => _ = new UiModifier { Key = "m", Background = "#fff", Child = new UiStack { Key = "s" } });
			Assert.DoesNotThrow(() => UiBackground.Solid("#A0b1C2"));
		});
	}
}
