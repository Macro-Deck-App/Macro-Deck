using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Localization;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Testing;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Widgets.ActionButton;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

/// <summary>
/// The Action Button widget's <c>widget-config</c> tree (issue #791/#837): every root and per-state key the
/// shipped v2 schema declares, states addressed by their own stable id and never by position, the
/// stateMode-driven split between root and per-state appearance, the provider-driven unavailability rules,
/// and the provider's decline of a foreign widget type.
/// </summary>
[TestFixture]
public class ActionButtonWidgetConfigTests
{
	private static readonly object _twoStates = new
	{
		stateMode = true,
		states = new object[]
		{
			new { id = "off", label = "Off" },
			new { id = "on", label = "On" },
		},
	};

	[Test]
	public void Editing_every_root_control_still_validates_against_the_shipped_schema()
	{
		var host = Render(new { });

		host.ById("label").Change("Press me");
		host.ById("fontFaceId").Change("inter-400");
		host.ById("fontSize").Change(20d);
		host.ById("textAlign").Change("left");
		host.ById("labelPosition").Change("top");
		host.ById("labelColor").Change("#111111");
		host.ById("backgroundColor").Change("#222222");
		host.ById("icon").Change(new { type = "icon-pack", reference = "bolt" });
		host.ById("iconDisplay")
			.Change(new { fit = "cover", zoom = 150d, offsetX = 10d, offsetY = -10d, opacity = 80d });
		host.ById("border.style").Change("comet");
		host.ById("border.color").Change("#00ff00");

		var composed = ComposeRoot(host);

		AssertValidatesAgainstSchema(composed);

		Assert.Multiple(() =>
		{
			Assert.That(composed.GetProperty("label").GetString(), Is.EqualTo("Press me"));
			Assert.That(composed.GetProperty("fontFaceId").GetString(), Is.EqualTo("inter-400"));
			Assert.That(composed.GetProperty("fontSize").GetDouble(), Is.EqualTo(20));
			Assert.That(composed.GetProperty("textAlign").GetString(), Is.EqualTo("left"));
			Assert.That(composed.GetProperty("labelPosition").GetString(), Is.EqualTo("top"));
			Assert.That(composed.GetProperty("labelColor").GetString(), Is.EqualTo("#111111"));
			Assert.That(composed.GetProperty("backgroundColor").GetString(), Is.EqualTo("#222222"));
			Assert.That(composed.GetProperty("icon").GetProperty("reference").GetString(), Is.EqualTo("bolt"));
			Assert.That(composed.GetProperty("iconDisplay").GetProperty("zoom").GetDouble(), Is.EqualTo(150));
			Assert.That(composed.GetProperty("border").GetProperty("style").GetString(), Is.EqualTo("comet"));
			Assert.That(composed.GetProperty("border").GetProperty("color").GetString(), Is.EqualTo("#00ff00"));
		});
	}

	[Test]
	public void States_round_trip_by_id_through_edit_reorder_and_delete()
	{
		var host = Render(_twoStates);

		// Edit "on"'s appearance - selecting it first, the way the state picker is meant to be used.
		host.ById("activeStateId").Change("on");
		host.ById("states.on.appearance.backgroundColor").Change("#ff0000");
		host.ById("states.on.appearance.label").Change("On label");

		var beforeReorder = ReadStates(host);
		var offBeforeReorder = beforeReorder.First(s => Id(s) == "off");
		var onAfterEdit = beforeReorder.First(s => Id(s) == "on");

		Assert.That(onAfterEdit.GetProperty("appearance").GetProperty("backgroundColor").GetString(),
			Is.EqualTo("#ff0000"));
		Assert.That(offBeforeReorder.TryGetProperty("appearance", out _), Is.False, "'off' was never edited.");

		// Reorder: "on" first. The whole-array binding is what a drag-reorder renderer would drive.
		var reordered = new JsonArray
		{
			JsonNode.Parse(onAfterEdit.GetRawText()), JsonNode.Parse(offBeforeReorder.GetRawText()),
		};
		host.ById("states").Change(JsonSerializer.Deserialize<JsonElement>(reordered.ToJsonString()));

		var afterReorder = ReadStates(host);

		Assert.Multiple(() =>
		{
			Assert.That(Id(afterReorder[0]), Is.EqualTo("on"));
			Assert.That(Id(afterReorder[1]), Is.EqualTo("off"));
			Assert.That(afterReorder[0].GetProperty("appearance").GetProperty("backgroundColor").GetString(),
				Is.EqualTo("#ff0000"));
			Assert.That(JsonNode.DeepEquals(JsonNode.Parse(afterReorder[1].GetRawText()),
					JsonNode.Parse(offBeforeReorder.GetRawText())),
				Is.True,
				"'off' must be untouched and carry no new key.");
		});

		// The resulting draft as a whole still validates.
		AssertValidatesAgainstSchema(ComposeRootWithStates(host));

		// Delete "on" - still the selected state, via the state row's "Manage this state" menu.
		host.ById("root.properties.state-row.manageState").Activate();
		host.ById("root.properties.deleteState").Activate();

		var afterDelete = ReadStates(host);
		Assert.That(afterDelete.Select(Id), Is.EqualTo(new[] { "off" }));
	}

	[Test]
	public void Deleting_a_state_removes_it_from_state_mapping_rules_and_the_fallback()
	{
		var host = Render(new
		{
			stateMode = true,
			states = new object[] { new { id = "off", label = "Off" }, new { id = "on", label = "On" } },
			stateMapping = new
			{
				rules = new object[] { new { id = "r1", stateId = "on", when = new { } } }, fallbackStateId = "on",
			},
		});

		host.ById("activeStateId").Change("on");
		host.ById("root.properties.state-row.manageState").Activate();
		host.ById("root.properties.deleteState").Activate();

		var mapping = host.ById("stateMapping").Property(UiConfigProperties.Value)!.Value;
		var stillReferencesOn = mapping.GetProperty("rules").EnumerateArray()
			.Any(r => r.GetProperty("stateId").GetString() == "on");

		Assert.That(stillReferencesOn, Is.False);
		Assert.That(mapping.GetProperty("fallbackStateId").GetString(), Is.Not.EqualTo("on"));
	}

	// The first switch to multi state has to leave a button with two usable faces, not an empty list the
	// user has to populate by hand. "off" carries no background of its own so it falls through to the
	// reader's accent, and "on" carries one, so the pair reads as two visibly different faces.
	[Test]
	public void Turning_on_state_mode_seeds_an_Off_and_On_pair_when_the_button_has_no_states()
	{
		var host = Render(new { stateMode = false, label = "Mute" });

		host.ById("stateMode").Change(true);

		var states = ReadStates(host);

		Assert.Multiple(() =>
		{
			Assert.That(states.Select(Id), Is.EqualTo(new[] { "off", "on" }));
			Assert.That(states.Select(s => s.GetProperty("label").GetString()), Is.EqualTo(new[] { "Off", "On" }));
			Assert.That(host.ById("activeStateId").Text(UiConfigProperties.Value), Is.EqualTo("off"));

			Assert.That(states[0].GetProperty("appearance").TryGetProperty("backgroundColor", out _),
				Is.False,
				"Off falls through to the reader's own accent rather than carrying a colour.");
			Assert.That(states[1].GetProperty("appearance").GetProperty("backgroundColor").GetString(),
				Is.Not.Null.And.Not.Empty);

			// Both inherit the caption the button already had, so gaining states never blanks its label.
			Assert.That(states.Select(s => s.GetProperty("appearance").GetProperty("label").GetString()),
				Is.EqualTo(new[] { "Mute", "Mute" }));
		});
	}

	// Turning state mode off leaves the states dormant in the stored data rather than deleting them, so
	// turning it back on must read them back instead of overwriting the user's work with the defaults.
	[Test]
	public void Turning_state_mode_on_again_keeps_the_states_the_button_already_has()
	{
		var host = Render(new
		{
			stateMode = false,
			states = new object[] { new { id = "s1", label = "Armed" }, new { id = "s2", label = "Idle" } },
		});

		host.ById("stateMode").Change(true);

		Assert.That(ReadStates(host).Select(s => s.GetProperty("label").GetString()),
			Is.EqualTo(new[] { "Armed", "Idle" }));
	}

	[Test]
	public void Appearance_writes_root_keys_when_stateMode_is_off_and_per_state_keys_when_it_is_on()
	{
		var offHost = Render(new { stateMode = false });

		offHost.ById("label").Change("Root label");
		offHost.ById("backgroundColor").Change("#abcdef");

		Assert.Multiple(() =>
		{
			Assert.That(ComposeRoot(offHost).GetProperty("label").GetString(), Is.EqualTo("Root label"));
			Assert.That(offHost.FindById("states"), Is.Null, "No state list while stateMode is off.");
		});

		var onHost = Render(_twoStates);

		onHost.ById("states.off.appearance.backgroundColor").Change("#123456");

		Assert.Multiple(() =>
		{
			Assert.That(onHost.FindById("label"),
				Is.Null,
				"The root appearance fields are gone while stateMode is on.");

			var off = ReadStates(onHost).First(s => Id(s) == "off");
			Assert.That(off.GetProperty("appearance").GetProperty("backgroundColor").GetString(),
				Is.EqualTo("#123456"));
		});
	}

	[Test]
	public void State_mapping_is_a_single_high_level_control_offering_the_current_states()
	{
		var host = Render(_twoStates);

		var node = host.ById("stateMapping");

		Assert.That(node.Type, Is.EqualTo(UiConfigPrimitives.StateMappingEditor));

		var offeredStateIds = node.Property(UiConfigProperties.States)!.Value.EnumerateArray()
			.Select(option => option.GetProperty("value").GetString())
			.ToList();

		Assert.That(offeredStateIds, Is.EquivalentTo(new[] { "off", "on" }));

		var mapping = new
		{
			rules = new object[] { new { id = "r1", stateId = "on", when = new { } } }, fallbackStateId = "off",
		};
		node.Change(mapping);

		Assert.Multiple(() =>
		{
			var value = host.ById("stateMapping").Property(UiConfigProperties.Value)!.Value;
			Assert.That(value.GetProperty("rules").GetArrayLength(), Is.EqualTo(1));
			Assert.That(value.GetProperty("fallbackStateId").GetString(), Is.EqualTo("off"));
			AssertValidatesAgainstSchema(ComposeRootWithStates(host));
		});
	}

	[Test]
	public void Delete_is_unavailable_below_two_states()
	{
		// Delete lives behind the state row's "Manage this state" menu (issue #837), so opening it is what
		// both hosts need before this can tell "unavailable" from "not yet revealed".
		var soloHost = Render(new
		{
			stateMode = true, states = new object[] { new { id = "solo", label = "Solo" } },
		});
		soloHost.ById("root.properties.state-row.manageState").Activate();
		var twoStateHost = Render(_twoStates);
		twoStateHost.ById("root.properties.state-row.manageState").Activate();

		Assert.Multiple(() =>
		{
			Assert.That(soloHost.FindById("root.properties.deleteState"), Is.Null);
			Assert.That(twoStateHost.FindById("root.properties.deleteState"), Is.Not.Null);
		});
	}

	[Test]
	public void State_management_is_unavailable_while_a_state_provider_is_set()
	{
		var host = Render(new
		{
			stateMode = true,
			states = new object[] { new { id = "muted", label = "Muted" }, new { id = "unmuted", label = "Unmuted" } },
			stateProvider = new { blockId = "b1", integrationId = "int", actionId = "act", actionLabel = "Mute" },
		});

		Assert.Multiple(() =>
		{
			Assert.That(host.FindById("activeStateId"), Is.Null);
			Assert.That(host.FindById("root.properties.state-row.addState"), Is.Null);
			Assert.That(host.FindById("states"), Is.Null);
			Assert.That(host.FindById("stateMapping"), Is.Null);
			Assert.That(host.FindById("root.properties.removeStateProvider"), Is.Not.Null);
		});
	}

	[Test]
	public void The_icon_control_is_unavailable_while_an_icon_provider_is_set()
	{
		var offHost = Render(new
		{
			stateMode = false, iconProvider = new { blockId = "b1", integrationId = "int", actionId = "act" },
		});

		Assert.That(offHost.FindById("icon"), Is.Null);

		var onHost = Render(new
		{
			stateMode = true,
			states = new object[] { new { id = "off", label = "Off" }, new { id = "on", label = "On" } },
			iconProvider = new { blockId = "b1", integrationId = "int", actionId = "act" },
		});

		Assert.That(onHost.FindById("states.off.appearance.icon"), Is.Null);
	}

	[Test]
	public void Removing_a_state_provider_restores_the_manual_state_backup()
	{
		var host = Render(new
		{
			stateMode = true,
			states = new object[] { new { id = "muted", label = "Muted" }, new { id = "unmuted", label = "Unmuted" } },
			stateProvider = new { blockId = "b1", integrationId = "int", actionId = "act", actionLabel = "Mute" },
			manualStateBackup = new
			{
				states = new object[]
				{
					new { id = "off", label = "Off", appearance = new { backgroundColor = "#000000" } },
					new { id = "on", label = "On" },
				},
				activeStateId = "off",
			},
		});

		host.ById("root.properties.removeStateProvider").Activate();

		var restored = ReadStates(host);

		Assert.Multiple(() =>
		{
			Assert.That(restored.Select(Id), Is.EqualTo(new[] { "off", "on" }));
			Assert.That(restored[0].GetProperty("appearance").GetProperty("backgroundColor").GetString(),
				Is.EqualTo("#000000"));
			Assert.That(host.FindById("root.properties.removeStateProvider"),
				Is.Null,
				"No provider is active any more.");
		});
	}

	[Test]
	public void A_state_with_no_border_of_its_own_shows_the_off_style_rather_than_nothing()
	{
		// An enumerated control whose value matches no option renders with nothing selected, which reads
		// as broken rather than as "no border" - the root section's own border names "off" for the same
		// reason.
		var host = Render(_twoStates);

		Assert.That(host.ById("states.off.appearance.border.style").Text(UiConfigProperties.Value),
			Is.EqualTo("off"));
	}

	// Issue #673: a button with hundreds of states stops being editable at all, so the editor stops
	// offering to add one at the limit. UiConfigButton has no disabled state, hence gone rather than dimmed.
	[Test]
	public void A_state_can_still_be_added_one_below_the_limit()
	{
		var host = Render(StatesData(ActionButtonStateModel.MaxStates - 1));

		Assert.Multiple(() =>
		{
			Assert.That(host.FindById("root.properties.state-row.addState"), Is.Not.Null);
			Assert.That(host.FindById("root.properties.state-limit"), Is.Null);
		});
	}

	[Test]
	public void At_the_limit_no_state_can_be_added_and_the_editor_says_why()
	{
		var host = Render(StatesData(ActionButtonStateModel.MaxStates));

		Assert.Multiple(() =>
		{
			Assert.That(host.FindById("root.properties.state-row.addState"), Is.Null);
			Assert.That(host.FindById("root.properties.state-limit"), Is.Not.Null);
		});
	}

	// The transition is the part that can actually break: one dispatch adds a state, removes the add
	// control and inserts the notice, and UiTestHost applies that patch under the client's own rules.
	[Test]
	public void Adding_the_last_allowed_state_removes_the_add_control_and_shows_the_notice()
	{
		var host = Render(StatesData(ActionButtonStateModel.MaxStates - 1));

		host.ById("root.properties.state-row.addState").Activate();

		Assert.Multiple(() =>
		{
			Assert.That(ReadStates(host), Has.Count.EqualTo(ActionButtonStateModel.MaxStates));
			Assert.That(host.FindById("root.properties.state-row.addState"), Is.Null);
			Assert.That(host.FindById("root.properties.state-limit"), Is.Not.Null);
		});
	}

	// A button configured before the limit existed keeps opening, with its states intact: the limit is
	// enforced where states are created, never on the way in.
	[Test]
	public void A_button_stored_far_over_the_limit_still_builds_its_editor()
	{
		var host = Render(StatesData(400));

		Assert.Multiple(() =>
		{
			Assert.That(ReadStates(host), Has.Count.EqualTo(400));
			Assert.That(host.FindById("root.properties.state-row.addState"), Is.Null);
			Assert.That(host.FindById("root.properties.state-limit"), Is.Not.Null);
			Assert.That(host.FindById("activeStateId"), Is.Not.Null);
		});
	}

	[Test]
	public void Adding_a_state_selects_it_and_produces_a_patch_the_renderer_can_apply()
	{
		// UiTestHost applies every patch the view produces with the same duplicate-id rule the client's
		// applyUiPatch enforces, so this fails outright if the arriving state's chrome shares an id with
		// the state it is replacing - a patch inserts before it removes.
		var host = Render(_twoStates);

		host.ById("root.properties.state-row.addState").Activate();

		var stateIds = ReadStates(host).Select(Id).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(stateIds, Has.Count.EqualTo(3));
			Assert.That(stateIds.Take(2), Is.EqualTo(new[] { "off", "on" }), "the existing states are untouched");
			Assert.That(host.ById("activeStateId").Text(UiConfigProperties.Value),
				Is.EqualTo(stateIds[2]),
				"the new state is the one being edited");
			Assert.That(host.FindById($"states.{stateIds[2]}.appearance"),
				Is.Not.Null,
				"and it is the state whose appearance form is showing");
		});
	}

	[Test]
	public void Selecting_another_state_moves_the_appearance_form_without_a_duplicate_id()
	{
		var host = Render(_twoStates);

		host.ById("activeStateId").Change("on");

		Assert.Multiple(() =>
		{
			Assert.That(host.FindById("states.on.appearance"), Is.Not.Null);
			Assert.That(host.FindById("states.off.appearance"), Is.Null);
		});
	}

	[Test]
	public void Cycle_states_on_tap_is_offered_only_while_nothing_else_governs_the_state()
	{
		var manual = Render(_twoStates);
		var singleState = Render(new { stateMode = false });
		var mapped = Render(new
		{
			stateMode = true,
			states = new object[] { new { id = "off", label = "Off" }, new { id = "on", label = "On" } },
			stateMapping = new
			{
				rules = new object[] { new { id = "r1", stateId = "on", when = new { } } }, fallbackStateId = "off",
			},
		});
		var provided = Render(new
		{
			stateMode = true,
			states = new object[] { new { id = "off", label = "Off" }, new { id = "on", label = "On" } },
			stateProvider = new { blockId = "b1", integrationId = "int", actionId = "act", actionLabel = "Mute" },
		});

		Assert.Multiple(() =>
		{
			Assert.That(manual.FindById("cycleStatesOnPress"), Is.Not.Null);
			Assert.That(singleState.FindById("cycleStatesOnPress"), Is.Null);
			Assert.That(mapped.FindById("cycleStatesOnPress"), Is.Null, "A mapping is authoritative for the state.");
			Assert.That(provided.FindById("cycleStatesOnPress"), Is.Null, "A provider is authoritative for the state.");
		});
	}

	[Test]
	public void Cycle_states_on_tap_starts_on_and_composes_the_turned_off_choice_as_a_boolean()
	{
		var host = Render(_twoStates);

		Assert.That(host.ById("cycleStatesOnPress").Property(UiConfigProperties.Value)!.Value.GetBoolean(),
			Is.True,
			"a button that has never said otherwise cycles on tap");

		host.ById("cycleStatesOnPress").Change(false);

		var composed = ComposeRootWithStates(host);

		Assert.Multiple(() =>
		{
			Assert.That(composed.GetProperty("cycleStatesOnPress").GetBoolean(), Is.False);
			AssertValidatesAgainstSchema(composed);
		});
	}

	[Test]
	public void The_actions_editor_offers_the_states_being_drafted_rather_than_only_the_saved_ones()
	{
		// The host's own options endpoint answers a "Set Button State" block from stored widget data, so
		// an edit made in this session is invisible to it until Save - the editor carries the draft list
		// on the actions node instead.
		var host = Render(_twoStates);
		host.ById("activeStateId").Change("on");
		host.ById("root.properties.state-row.manageState").Activate();
		host.ById("states.on.label").Change("Recording");

		Assert.Multiple(() =>
		{
			Assert.That(OfferedFlowStates(host).Select(o => o.Value), Is.EqualTo(new[] { "off", "on" }));
			Assert.That(OfferedFlowStates(host).Select(o => o.Label), Is.EqualTo(new[] { "Off", "Recording" }));
		});
	}

	[Test]
	public void The_actions_editor_offers_no_states_while_the_button_has_a_single_appearance()
	{
		var host = Render(new { stateMode = false });

		Assert.That(OfferedFlowStates(host), Is.Empty);
	}

	[Test]
	public void OnStateChange_is_among_the_actions_editors_triggers_only_when_stateMode_is_on()
	{
		var off = Render(new { stateMode = false });
		var on = Render(_twoStates);

		Assert.Multiple(() =>
		{
			Assert.That(TriggersOf(off), Does.Not.Contain("onStateChange"));
			Assert.That(TriggersOf(on), Does.Contain("onStateChange"));
			Assert.That(TriggersOf(on), Is.SupersetOf(TriggersOf(off)), "Turning it on only adds a trigger.");
		});
	}

	[Test]
	public void Appearance_controls_sit_under_three_tabs_with_no_cross_tab_leakage()
	{
		var host = Render(new { });

		var tabs = host.SingleByType(UiConfigPrimitives.Tabs);

		Assert.That(tabs.Children, Has.Count.EqualTo(3));
		Assert.That(tabs.Children.Select(t => t.Type), Is.All.EqualTo(UiConfigPrimitives.Tab));

		var labelTab = tabs.Children.First(t => DescendantIds(t).Contains("label"));
		var backgroundTab = tabs.Children.First(t => DescendantIds(t).Contains("backgroundColor"));
		var borderTab = tabs.Children.First(t => DescendantIds(t).Contains("border"));

		Assert.Multiple(() =>
		{
			Assert.That(labelTab, Is.Not.SameAs(backgroundTab));
			Assert.That(labelTab, Is.Not.SameAs(borderTab));
			Assert.That(backgroundTab, Is.Not.SameAs(borderTab));

			var labelIds = DescendantIds(labelTab);
			Assert.That(labelIds,
				Is.SupersetOf(new[] { "label", "fontFaceId", "fontSize", "textAlign", "labelPosition", "labelColor" }));
			Assert.That(labelIds, Has.No.Member("backgroundColor"));
			Assert.That(labelIds, Has.No.Member("icon"));
			Assert.That(labelIds, Has.No.Member("border"));

			var backgroundIds = DescendantIds(backgroundTab);
			Assert.That(backgroundIds, Is.SupersetOf(new[] { "backgroundColor", "icon" }));
			Assert.That(backgroundIds, Has.No.Member("label"));
			Assert.That(backgroundIds, Has.No.Member("border"));

			var borderIds = DescendantIds(borderTab);
			Assert.That(borderIds, Does.Contain("border"));
			Assert.That(borderIds, Has.No.Member("label"));
			Assert.That(borderIds, Has.No.Member("backgroundColor"));
			Assert.That(borderIds, Has.No.Member("icon"));
		});
	}

	[Test]
	public void Switching_single_and_multi_state_writes_stateMode_as_a_boolean_and_gates_multi_only_sections()
	{
		var host = Render(new { });

		Assert.Multiple(() =>
		{
			Assert.That(host.ById("stateMode").Type,
				Is.EqualTo(UiConfigPrimitives.Boolean),
				"a choice cannot carry the boolean the schema declares - see issue #837");
			Assert.That(host.ById("stateMode").Flag(UiConfigProperties.Value), Is.False);
			Assert.That(host.FindById("states"), Is.Null);
			Assert.That(host.FindById("stateMapping"), Is.Null);
			Assert.That(TriggersOf(host), Does.Not.Contain("onStateChange"));
		});

		var toMulti = host.ById("stateMode").Change(true);
		Assert.That(toMulti.IsAccepted, Is.True);

		Assert.Multiple(() =>
		{
			Assert.That(host.ById("stateMode").Flag(UiConfigProperties.Value), Is.True);
			Assert.That(host.FindById("states"), Is.Not.Null);
			Assert.That(host.FindById("stateMapping"), Is.Not.Null);
			Assert.That(TriggersOf(host), Does.Contain("onStateChange"));
		});

		host.ById("stateMode").Change(false);

		Assert.Multiple(() =>
		{
			Assert.That(host.ById("stateMode").Flag(UiConfigProperties.Value), Is.False);
			Assert.That(host.FindById("states"), Is.Null);
			Assert.That(host.FindById("stateMapping"), Is.Null);
			Assert.That(TriggersOf(host), Does.Not.Contain("onStateChange"));
		});
	}

	[Test]
	public void Switching_to_multi_state_composes_stateMode_as_a_json_boolean_that_validates_against_the_schema()
	{
		var host = Render(new { });

		host.ById("stateMode").Change(true);

		// The bug this guards against: a segmented choice's own value is the string "multi", which is
		// truthy - so the composed draft has to be checked for its JSON *type*, not merely for whether it
		// is set, or a regression back to a string value would pass unnoticed.
		var stateModeValue = host.ById("stateMode").Property(UiConfigProperties.Value);
		Assert.That(stateModeValue?.ValueKind, Is.EqualTo(JsonValueKind.True));

		var composed = ComposeRootWithStates(host);

		Assert.That(composed.GetProperty("stateMode").ValueKind, Is.EqualTo(JsonValueKind.True));
		AssertValidatesAgainstSchema(composed);
	}

	[Test]
	public void Root_iconDisplay_is_one_node_present_only_once_an_icon_is_set()
	{
		var host = Render(new { });

		Assert.That(host.FindById("iconDisplay"), Is.Null, "No icon yet, so nothing to frame.");

		host.ById("icon").Change(new { type = "icon-pack", reference = "bolt" });

		Assert.That(host.FindById("iconDisplay"), Is.Not.Null);

		host.ById("iconDisplay")
			.Change(new { fit = "cover", zoom = 150d, offsetX = 10d, offsetY = -10d, opacity = 80d });

		var value = host.ById("iconDisplay").Property(UiConfigProperties.Value)!.Value;

		Assert.Multiple(() =>
		{
			Assert.That(value.GetProperty("fit").GetString(), Is.EqualTo("cover"));
			Assert.That(value.GetProperty("zoom").GetDouble(), Is.EqualTo(150));
			Assert.That(value.GetProperty("offsetX").GetDouble(), Is.EqualTo(10));
			Assert.That(value.GetProperty("offsetY").GetDouble(), Is.EqualTo(-10));
			Assert.That(value.GetProperty("opacity").GetDouble(), Is.EqualTo(80));
		});

		AssertValidatesAgainstSchema(ComposeRoot(host));
	}

	[Test]
	public void Per_state_iconDisplay_is_one_node_present_only_once_that_state_has_an_icon()
	{
		var host = Render(_twoStates);
		host.ById("activeStateId").Change("on");

		Assert.That(host.FindById("states.on.appearance.iconDisplay"), Is.Null);
		Assert.That(host.FindById("states.off.appearance.iconDisplay"), Is.Null);

		host.ById("states.on.appearance.icon").Change(new { type = "icon-pack", reference = "bolt" });

		Assert.Multiple(() =>
		{
			Assert.That(host.FindById("states.on.appearance.iconDisplay"), Is.Not.Null);
			Assert.That(host.FindById("states.off.appearance.iconDisplay"), Is.Null, "'off' has no icon of its own.");
		});

		host.ById("states.on.appearance.iconDisplay")
			.Change(new { fit = "cover", zoom = 200d, offsetX = 5d, offsetY = 5d, opacity = 90d });

		var value = host.ById("states.on.appearance.iconDisplay").Property(UiConfigProperties.Value)!.Value;
		Assert.That(value.GetProperty("zoom").GetDouble(), Is.EqualTo(200));
	}

	[Test]
	public void Headings_and_tabs_are_chrome_that_contributes_no_keys()
	{
		var host = Render(new { });

		foreach (var heading in host.ByType(UiConfigPrimitives.Heading))
		{
			Assert.That(heading.HasProperty(UiConfigProperties.Value),
				Is.False,
				$"'{heading.Id}' is a heading - it must never carry a data value.");
		}

		foreach (var tabsOrTab in host.ByType(UiConfigPrimitives.Tabs).Concat(host.ByType(UiConfigPrimitives.Tab)))
		{
			Assert.That(tabsOrTab.HasProperty(UiConfigProperties.Value),
				Is.False,
				$"'{tabsOrTab.Id}' is tab chrome - it must never carry a data value.");
		}

		host.ById("icon").Change(new { type = "icon-pack", reference = "bolt" });
		host.ById("label").Change("Press me");
		host.ById("fontFaceId").Change("inter-400");
		host.ById("backgroundColor").Change("#222222");
		host.ById("iconDisplay")
			.Change(new { fit = "cover", zoom = 150d, offsetX = 10d, offsetY = -10d, opacity = 80d });
		host.ById("border.style").Change("comet");

		var composed = ComposeRoot(host);
		AssertValidatesAgainstSchema(composed);

		Assert.That(composed.EnumerateObject().Select(p => p.Name),
			Is.EquivalentTo(new[]
			{
				"label", "fontFaceId", "fontSize", "textAlign", "labelPosition", "labelColor", "backgroundColor",
				"icon",
				"iconDisplay", "border",
			}),
			"Only real schema keys should appear in the draft - no heading or tab contributed one.");
	}

	[Test]
	public void The_label_field_does_not_set_literalOnly_so_it_keeps_the_variable_helper()
	{
		var rootHost = Render(new { });
		Assert.That(rootHost.ById("label").HasProperty(UiConfigProperties.LiteralOnly), Is.False);

		var stateHost = Render(_twoStates);
		stateHost.ById("activeStateId").Change("on");
		Assert.That(stateHost.ById("states.on.appearance.label").HasProperty(UiConfigProperties.LiteralOnly), Is.False);
	}

	// ---- font row (issue #837): Font full width, then Style and Size sharing a row -------------------------

	private static readonly FontFaceInfo _interRegular =
		new("inter-400", "Inter", 400, 5, "upright", "Regular", RemoteRenderable: true);

	private static readonly FontFaceInfo _interBold =
		new("inter-700", "Inter", 700, 5, "upright", "Bold", RemoteRenderable: true);

	private static readonly FontFaceInfo _acmeRegular =
		new("acme-400", "Acme", 400, 5, "upright", "Regular", RemoteRenderable: true);

	[Test]
	public void The_font_family_control_is_transient_and_offers_every_family()
	{
		var host = Render(new { fontFaceId = "inter-400" },
			fonts: new FakeFontCatalog(_interRegular, _interBold, _acmeRegular));

		var family = host.ById("fontFamily");

		Assert.Multiple(() =>
		{
			Assert.That(family.Flag(UiConfigProperties.Transient), Is.True);
			Assert.That(family.Text(UiConfigProperties.Value), Is.EqualTo("Inter"));
			var familyNames = family.Property(UiConfigProperties.Options)!.Value.EnumerateArray()
				.Select(o => o.GetProperty("value").GetString()).ToList();
			Assert.That(familyNames, Is.EquivalentTo(new[] { "", "Inter", "Acme" }));
		});
	}

	[Test]
	public void Picking_a_family_resolves_it_to_that_familys_first_real_fontFaceId()
	{
		// The family control's own write never reaches the draft - config-draft.util.spec.ts covers that,
		// client-side, where the skip actually happens (UiInput.Transient). What the host owns is resolving
		// a family choice to a real fontFaceId, which is what this asserts.
		var host = Render(new { fontFaceId = "inter-400" },
			fonts: new FakeFontCatalog(_interRegular, _interBold, _acmeRegular));

		host.ById("fontFamily").Change("Acme");

		Assert.That(host.ById("fontFaceId").Text(UiConfigProperties.Value), Is.EqualTo("acme-400"));

		var composed = ComposeRoot(host);
		Assert.That(composed.GetProperty("fontFaceId").GetString(), Is.EqualTo("acme-400"));
	}

	[Test]
	public void Style_offers_only_the_selected_familys_own_faces()
	{
		var host = Render(new { fontFaceId = "inter-400" },
			fonts: new FakeFontCatalog(_interRegular, _interBold, _acmeRegular));

		var styleOptions = host.ById("fontFaceId").Property(UiConfigProperties.Options)!.Value.EnumerateArray()
			.Select(o => o.GetProperty("value").GetString()).ToList();

		Assert.That(styleOptions, Is.EquivalentTo(new[] { "inter-400", "inter-700" }));
	}

	[Test]
	public void Font_starts_on_inherited_and_neither_dropdown_shows_an_unreachable_default()
	{
		var host = Render(new { }, fonts: new FakeFontCatalog(_interRegular, _interBold, _acmeRegular));

		var family = host.ById("fontFamily");
		var firstOption = family.Property(UiConfigProperties.Options)!.Value.EnumerateArray().First();

		Assert.Multiple(() =>
		{
			Assert.That(family.Text(UiConfigProperties.Value), Is.Empty);
			Assert.That(firstOption.GetProperty("value").GetString(), Is.Empty);
			Assert.That(firstOption.GetProperty("label").GetRawText(),
				Does.Contain("Forms.InheritableSetting.Inherited"));
			Assert.That(family.HasProperty(UiConfigProperties.Placeholder), Is.False);
			Assert.That(host.ById("fontFaceId").HasProperty(UiConfigProperties.Placeholder), Is.False);
		});
	}

	[Test]
	public void Choosing_inherited_removes_the_buttons_own_font()
	{
		var host = Render(new { fontFaceId = "inter-400" },
			fonts: new FakeFontCatalog(_interRegular, _interBold, _acmeRegular));

		host.ById("fontFamily").Change("");

		Assert.Multiple(() =>
		{
			Assert.That(host.ById("fontFamily").Text(UiConfigProperties.Value), Is.Empty);
			Assert.That(host.ById("fontFaceId").Text(UiConfigProperties.Value), Is.Empty);
		});
	}

	[Test]
	public void A_font_that_is_no_longer_installed_can_still_be_reset_to_inherited()
	{
		var host = Render(new { fontFaceId = "gone-400" },
			fonts: new FakeFontCatalog(_interRegular, _interBold, _acmeRegular));

		Assert.That(host.ById("fontFamily").Text(UiConfigProperties.Value), Is.Not.Empty);

		host.ById("fontFamily").Change("");

		Assert.That(host.ById("fontFaceId").Text(UiConfigProperties.Value), Is.Empty);
	}

	[Test]
	public void Style_is_disabled_until_a_font_is_chosen()
	{
		var host = Render(new { }, fonts: new FakeFontCatalog(_interRegular, _interBold, _acmeRegular));

		Assert.That(host.ById("fontFaceId").Flag(UiConfigProperties.Disabled), Is.True);

		host.ById("fontFamily").Change("Inter");

		Assert.That(host.ById("fontFaceId").Flag(UiConfigProperties.Disabled), Is.False);
	}

	[Test]
	public void Choosing_inherited_on_a_state_removes_that_states_own_font()
	{
		var host = Render(_twoStates, fonts: new FakeFontCatalog(_interRegular, _interBold, _acmeRegular));
		host.ById("activeStateId").Change("on");
		host.ById("states.on.appearance.fontFamily").Change("Inter");

		host.ById("states.on.appearance.fontFamily").Change("");

		var on = ReadStates(host).First(s => Id(s) == "on");
		var hasOwnFont = on.TryGetProperty("appearance", out var appearance) &&
			appearance.TryGetProperty("fontFaceId", out _);
		Assert.That(hasOwnFont, Is.False);
	}

	// ---- the state row's "Manage this state" menu (issue #837) ----------------------------------------------

	[Test]
	public void The_third_state_row_control_is_a_manage_menu_not_a_delete_button()
	{
		var host = Render(_twoStates);

		var manage = host.ById("root.properties.state-row.manageState");

		Assert.Multiple(() =>
		{
			Assert.That(manage.Text(UiConfigProperties.Icon), Is.EqualTo("dots-vertical"));
			// Label crosses as a localized reference, resolved client-side (S11 in WeatherWidgetViewTests
			// documents the same shape) - asserting the raw reference is what keeps this test from silently
			// passing against whatever English happens to say today.
			Assert.That(manage.Property(UiConfigProperties.Label)!.Value.GetRawText(),
				Is.EqualTo("""{"$localized":{"scope":"macrodeck.app","key":"Widgets.Editor.ManageThisState"}}"""));
			Assert.That(host.FindById("root.properties.deleteState"),
				Is.Null,
				"delete stays behind the menu until it is opened");
		});
	}

	[Test]
	public void Opening_the_manage_menu_reveals_rename_and_delete_hiding_delete_below_two_states()
	{
		var host = Render(_twoStates);

		Assert.That(host.FindById("states.off.label"), Is.Null, "rename is hidden until the menu is opened");

		host.ById("root.properties.state-row.manageState").Activate();

		Assert.Multiple(() =>
		{
			Assert.That(host.FindById("states.off.label"), Is.Not.Null);
			Assert.That(host.FindById("root.properties.deleteState"), Is.Not.Null);
		});

		host.ById("root.properties.deleteState").Activate();

		Assert.That(host.FindById("root.properties.deleteState"),
			Is.Null,
			"a stateful button keeps at least one state");
	}

	// ---- the live state line (issue #837) --------------------------------------------------------------------

	[Test]
	public void The_live_state_line_names_the_state_the_widget_is_actually_showing()
	{
		var host = Render(_twoStates, liveState: new WidgetStateOption("on", LocalizedText.FromLiteral("On")));

		var live = host.ById("root.properties.live-state");

		Assert.Multiple(() =>
		{
			Assert.That(live.Text(UiConfigProperties.Severity), Is.EqualTo("success"));
			// Text crosses as a localized reference with its {name} argument, resolved client-side - see the
			// same raw-reference assertion in WeatherWidgetViewTests's S11.
			Assert.That(live.Property(UiConfigProperties.Text)!.Value.GetRawText(),
				Is.EqualTo(
					"""{"$localized":{"scope":"macrodeck.app","key":"Widgets.Editor.CurrentlyState","arguments":{"name":"On"}}}"""));
		});
	}

	// The list is where a user looks for "which state is it on right now", so the answer is in the list -
	// the line under it only covers the case where the live state is not the one on screen.
	[Test]
	public void The_state_list_marks_the_state_the_widget_is_on_right_now()
	{
		var host = Render(_twoStates, liveState: new WidgetStateOption("on", LocalizedText.FromLiteral("On")));

		var options = host.ById("activeStateId").Property(UiConfigProperties.Options)!.Value;

		// Every option carries the key; only a marked one carries a value under it.
		var badges = options.EnumerateArray()
			.Select(option => (
				Id: option.GetProperty("value").GetString(),
				HasBadge: option.TryGetProperty("badge", out var badge) && badge.ValueKind is not JsonValueKind.Null))
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(badges.Single(o => o.Id == "on").HasBadge, Is.True);
			Assert.That(badges.Single(o => o.Id == "off").HasBadge, Is.False);
		});
	}

	// Said twice, it reads as two different facts. The list already badges the live state, so the line is
	// the answer only while the state being edited is a different one.
	[Test]
	public void The_live_state_line_is_absent_while_the_live_state_is_the_one_being_edited()
	{
		var host = Render(_twoStates, liveState: new WidgetStateOption("off", LocalizedText.FromLiteral("Off")));

		Assert.That(host.FindById("root.properties.live-state"), Is.Null);
	}

	[Test]
	public void The_live_state_line_is_absent_without_a_known_live_state_or_outside_multi_state()
	{
		Assert.That(Render(_twoStates).FindById("root.properties.live-state"), Is.Null);
		Assert.That(Render(new { }, liveState: new WidgetStateOption("off", LocalizedText.FromLiteral("Off")))
				.FindById("root.properties.live-state"),
			Is.Null,
			"Single state has no state row at all");
	}

	// ---- the label colour's reset affordance (issue #837) --------------------------------------------------

	[Test]
	public void Label_color_supports_reset_to_unset()
	{
		var host = Render(new { labelColor = "#123456" });

		var labelColor = host.ById("labelColor");

		Assert.Multiple(() =>
		{
			Assert.That(labelColor.Flag(UiConfigProperties.SupportsReset), Is.True);
			Assert.That(labelColor.Text(UiConfigProperties.DefaultValue), Is.EqualTo(string.Empty));
		});
	}

	[Test]
	public void Background_and_border_colours_support_reset_to_unset()
	{
		var host = Render(new { backgroundColor = "#123456", border = new { style = "static", color = "#654321" } });

		var backgroundColor = host.ById("backgroundColor");
		var borderColor = host.ById("border.color");

		Assert.Multiple(() =>
		{
			Assert.That(backgroundColor.Flag(UiConfigProperties.SupportsReset), Is.True);
			Assert.That(backgroundColor.Text(UiConfigProperties.DefaultValue), Is.EqualTo(string.Empty));
			Assert.That(borderColor.Flag(UiConfigProperties.SupportsReset), Is.True);
			Assert.That(borderColor.Text(UiConfigProperties.DefaultValue), Is.EqualTo(string.Empty));
		});
	}

	[Test]
	public async Task A_config_surface_naming_a_different_widget_type_is_declined()
	{
		// The decline check runs before any of the provider's other collaborators are touched (mirrors
		// SliderWidgetConfigTests's own decline test), so everything but the two it actually reads -
		// IIntegrationRegistry and IFontCatalog - can be null: this session is refused before construction
		// ever gets that far.
		var provider = new ActionButtonWidgetUiProvider(null!,
			null!,
			null!,
			null!,
			null!,
			new WidgetStateSubscriptionTracker(),
			new LabelSubscriptionTracker(),
			new LabelRenderChannel(),
			null!,
			null!,
			null!,
			null!,
			new FakeIntegrationRegistry(),
			new FakeFontCatalog());

		var surface = ConfigSurface(WidgetTypeIds.Clock, "{}");

		var session = await provider.CreateSessionAsync(new UiSessionRequest { Surface = surface, UiModelVersion = 1 },
			CancellationToken.None);

		Assert.That(session, Is.Null);
	}

	/// <summary>
	/// The framing preview draws the button, so it has to be shaped like the button: a widget two cells wide
	/// and one tall frames at 2, not at square. The ratio comes from the surface rather than the stored data
	/// because geometry belongs to the deck, and a configuration tree neither reads nor writes it.
	/// </summary>
	[Test]
	public void The_framing_preview_is_shaped_like_the_widget_on_the_deck()
	{
		var stored = new
		{
			icon = new { type = "icon-pack", reference = "bolt" },
			iconDisplay = new { fit = "contain", zoom = 100, offsetX = 0, offsetY = 0, opacity = 100 },
		};

		var wide = Render(stored, aspectRatio: 2);
		var square = Render(stored);

		Assert.Multiple(() =>
		{
			Assert.That(wide.ById("iconDisplay").Property(UiConfigProperties.AspectRatio)?.GetDouble(),
				Is.EqualTo(2));
			Assert.That(square.ById("iconDisplay").Property(UiConfigProperties.AspectRatio)?.GetDouble(),
				Is.EqualTo(1));
		});
	}

	// ---- helpers -----------------------------------------------------------------------------------------

	private static UiTestHost Render(
		object data,
		double aspectRatio = 1,
		WidgetStateOption? liveState = null,
		IFontCatalog? fonts = null)
		=> UiTestHost.Render(ActionButtonWidgetConfigView.Build(JsonSerializer.SerializeToElement(data),
			aspectRatio,
			new FakeIntegrationRegistry(),
			fonts ?? new FakeFontCatalog(),
			liveState));

	private static object StatesData(int count) => new
	{
		stateMode = true,
		states = Enumerable.Range(0, count)
			.Select(i => new { id = $"s{i}", label = $"State {i}" })
			.ToArray()
	};

	private static string Id(JsonElement state) => state.GetProperty("id").GetString()!;

	/// <summary>This node's own id plus every descendant's, depth-first - used to check which tab a control
	/// landed in without depending on how deep the chrome nests it.</summary>
	private static List<string> DescendantIds(UiTestNode node)
	{
		var ids = new List<string> { node.Id };

		foreach (var child in node.Children)
		{
			ids.AddRange(DescendantIds(child));
		}

		return ids;
	}

	private static List<JsonElement> ReadStates(UiTestHost host)
	{
		var value = host.ById("states").Property(UiConfigProperties.Value);

		return value is { ValueKind: JsonValueKind.Array } array ? [.. array.EnumerateArray()] : [];
	}

	private static List<(string Value, string Label)> OfferedFlowStates(UiTestHost host)
	{
		var states = host.ById("flows").Property(UiConfigProperties.States);

		return states is { ValueKind: JsonValueKind.Array } array
			? array.EnumerateArray()
				.Select(option => (option.GetProperty("value").GetString() ?? string.Empty,
					option.GetProperty("label").GetString() ?? string.Empty))
				.ToList()
			: [];
	}

	private static List<string> TriggersOf(UiTestHost host)
	{
		var triggers = host.ById("flows").Property(UiConfigProperties.Triggers);

		return triggers is { ValueKind: JsonValueKind.Array } array
			? array.EnumerateArray().Select(t => t.GetString() ?? string.Empty).ToList()
			: [];
	}

	private static JsonElement ComposeRoot(UiTestHost host)
	{
		var data = new Dictionary<string, object?>
		{
			["label"] = host.ById("label").Text(UiConfigProperties.Value),
			["fontFaceId"] = host.ById("fontFaceId").Text(UiConfigProperties.Value),
			["fontSize"] = host.ById("fontSize").Number(UiConfigProperties.Value),
			["textAlign"] = host.ById("textAlign").Text(UiConfigProperties.Value),
			["labelPosition"] = host.ById("labelPosition").Text(UiConfigProperties.Value),
			["labelColor"] = host.ById("labelColor").Text(UiConfigProperties.Value),
			["backgroundColor"] = host.ById("backgroundColor").Text(UiConfigProperties.Value),
			["icon"] = host.ById("icon").Property(UiConfigProperties.Value),
			["iconDisplay"] = host.FindById("iconDisplay")?.Property(UiConfigProperties.Value),
			["border"] = new Dictionary<string, object?>
			{
				["style"] = host.ById("border.style").Text(UiConfigProperties.Value),
				["color"] = host.ById("border.color").Text(UiConfigProperties.Value),
			},
		};

		return JsonSerializer.SerializeToElement(data);
	}

	private static JsonElement ComposeRootWithStates(UiTestHost host)
	{
		var root = new JsonObject
		{
			// Read from the node's own value, not hard-coded, so a regression back to the segmented
			// control's string value ("multi") shows up here rather than being papered over.
			["stateMode"]
				= JsonNode.Parse(host.ById("stateMode").Property(UiConfigProperties.Value)!.Value.GetRawText()),
			["states"] = JsonNode.Parse(host.ById("states").Property(UiConfigProperties.Value)!.Value.GetRawText()),
		};

		if (host.FindById("stateMapping")?.Property(UiConfigProperties.Value) is { } mapping)
		{
			root["stateMapping"] = JsonNode.Parse(mapping.GetRawText());
		}

		if (host.FindById("cycleStatesOnPress")?.Property(UiConfigProperties.Value) is { } cycle)
		{
			root["cycleStatesOnPress"] = JsonNode.Parse(cycle.GetRawText());
		}

		return JsonSerializer.Deserialize<JsonElement>(root.ToJsonString());
	}

	private static void AssertValidatesAgainstSchema(JsonElement composed)
	{
		var provider = new WidgetDataSchemaProvider(new WidgetTypeRegistry(new RecordingMediator()));

		Assert.That(provider.TryGet(WidgetTypeIds.ActionButton, out var schema), Is.True);
		Assert.That(WidgetDataSchema.Validate(schema!, composed), Is.Empty);
	}

	private static UiSurface ConfigSurface(string widgetType, string widgetData)
		=> new()
		{
			Kind = UiSurfaceKinds.Config,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiConfigSurfaceAttributes.EntryPoint]
					= JsonSerializer.SerializeToElement(UiConfigEntryPoints.WidgetConfig),
				[UiConfigSurfaceAttributes.WidgetId] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString()),
				[UiConfigSurfaceAttributes.WidgetType] = JsonSerializer.SerializeToElement(widgetType),
				[UiConfigSurfaceAttributes.WidgetData] = JsonSerializer.Deserialize<JsonElement>(widgetData),
			},
		};

	private sealed class FakeFontCatalog : IFontCatalog
	{
		private readonly List<FontFaceInfo> _faces;

		public FakeFontCatalog(params FontFaceInfo[] faces) => _faces = faces.ToList();

		public IReadOnlyList<FontFaceInfo> GetFaces() => _faces;

		public byte[]? GetFaceFile(string faceId) => null;
	}
}
