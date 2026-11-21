using System.Text.Json;
using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Widgets.ActionButton;

namespace MacroDeckHost.Tests.UnitTests.Widgets;

/// <summary>Acceptance Group A counterexamples: a typed icon reference the host cannot resolve into an
/// image must still leave the rest of the widget - and the stored reference itself - completely
/// unaffected.</summary>
[TestFixture]
public class ActionButtonWidgetDataTests
{
	[Test]
	public void NonGuidReference_RoundTripsCharacterIdenticalAndRendersLabelBackgroundBorderButNoImage()
	{
		var widget = new WidgetEntity
		{
			Id = Guid.NewGuid(),
			Type = WidgetTypeIds.ActionButton,
			Data = """
				   {"stateMode":false,"icon":{"type":"plugin-asset","reference":"spotify:album/4aawyAB9vmqN3uQ7FjRGTy"},
				    "label":"Now Playing","backgroundColor":"#112233","border":{"style":"static","color":"#00ff00"}}
				   """
		};
		var originalData = widget.Data;

		// No migration ever rewrites an already-typed reference of a provider it does not recognise -
		// scenario 5's "kills a closed enum for type" applies here too: an unknown type is accepted and
		// left exactly as it was saved.
		var changed = WidgetIconReferenceMigration.Normalize(widget);

		var config = ActionButtonWidgetData.Parse(JsonDocument.Parse(widget.Data!).RootElement);
		var icon = config.ResolveIcon(null);
		var appearance = config.Resolve(null);

		Assert.Multiple(() =>
		{
			Assert.That(changed, Is.False, "an unknown-provider reference must never be rewritten or blocked");
			Assert.That(widget.Data, Is.EqualTo(originalData), "the loaded object is character-identical");
			Assert.That(icon, Is.Not.Null);
			Assert.That(icon!.Icon,
				Is.EqualTo(new WidgetIconReference("plugin-asset", "spotify:album/4aawyAB9vmqN3uQ7FjRGTy")));
			Assert.That(icon.Icon.Type,
				Is.Not.EqualTo(WidgetIconReference.IconPackType),
				"an icon-pack-only source registry has nothing that answers for this type, so it renders no image");
			Assert.That(appearance.Label, Is.EqualTo("Now Playing"));
			Assert.That(appearance.BackgroundColor, Is.EqualTo("#112233"));
			Assert.That(appearance.Border, Is.Not.Null);
			Assert.That(appearance.Border!.Style, Is.EqualTo("static"));
		});
	}

	[Test]
	public void UnknownIconPackReference_KeepsResolvingToItsOwnOpaqueReferenceWhileOtherAppearanceStaysNormal()
	{
		// A GUID-shaped reference with no matching icon in the catalog behaves exactly as an unparseable
		// legacy iconId always has: WidgetIconResourcesTests.A_missing_icon_yields_no_image covers the
		// "no image" consequence once a real IIconService is asked for it; this covers that the reference
		// itself keeps parsing to a normal, opaque WidgetIconReference and never trips a GUID-format error.
		const string danglingReference = "0198dddd-0000-0000-0000-000000000000";
		var config = ActionButtonWidgetData.Parse(JsonDocument.Parse($$"""
																	   {"stateMode":false,"icon":{"type":"icon-pack","reference":"{{danglingReference}}"},
																	    "label":"Old","backgroundColor":"#333333","labelColor":"#eeeeee"}
																	   """)
			.RootElement);

		var icon = config.ResolveIcon(null);
		var appearance = config.Resolve(null);

		Assert.Multiple(() =>
		{
			Assert.That(icon!.Icon, Is.EqualTo(WidgetIconReference.IconPack(danglingReference)));
			Assert.That(appearance.Label, Is.EqualTo("Old"));
			Assert.That(appearance.BackgroundColor, Is.EqualTo("#333333"));
			Assert.That(appearance.LabelColor, Is.EqualTo("#eeeeee"));
		});
	}

	[Test]
	public void LabelBoxColor_IsAbsentForStoredDataWithoutTheKey_AndCascadesStateThenRoot()
	{
		var legacy = ActionButtonWidgetData.Parse(JsonDocument.Parse("""{"label":"Old","labelColor":"#eeeeee"}""")
			.RootElement);
		var staged = ActionButtonWidgetData.Parse(JsonDocument.Parse("""
																	 {"stateMode":true,"labelBoxColor":"#111111",
																	  "states":[{"id":"a","appearance":{"labelBoxColor":"#222222"}},{"id":"b","appearance":{"label":"B"}}]}
																	 """)
			.RootElement);

		Assert.Multiple(() =>
		{
			Assert.That(legacy.Resolve(null).LabelBoxColor, Is.Null);
			Assert.That(staged.Resolve("a").LabelBoxColor, Is.EqualTo("#222222"));
			Assert.That(staged.Resolve("b").LabelBoxColor, Is.EqualTo("#111111"));
		});
	}

	[Test]
	public void Label_styling_keeps_the_shadow_and_draws_no_outline_or_box_border_for_stored_data_without_the_keys()
	{
		var legacy = ActionButtonWidgetData
			.Parse(JsonDocument.Parse("""{"label":"Old","labelColor":"#eeeeee"}""").RootElement)
			.Resolve(null);

		Assert.Multiple(() =>
		{
			Assert.That(legacy.LabelShadow, Is.True);
			Assert.That(legacy.HasLabelOutline, Is.False);
			Assert.That(legacy.HasLabelBoxBorder, Is.False);
		});
	}

	[Test]
	public void Label_styling_cascades_state_then_root()
	{
		var staged = ActionButtonWidgetData.Parse(JsonDocument.Parse("""
																	 {"stateMode":true,"labelShadow":false,"labelOutlineColor":"#111111","labelOutlineWidth":2,
																	  "labelBoxBorderColor":"#333333","labelBoxBorderWidth":3,
																	  "states":[
																	    {"id":"a","appearance":{"labelShadow":true,"labelOutlineColor":"#222222","labelOutlineWidth":2.5,
																	      "labelBoxBorderColor":"#444444","labelBoxBorderWidth":1}},
																	    {"id":"b","appearance":{"label":"B"}}]}
																	 """)
			.RootElement);

		var a = staged.Resolve("a");
		var b = staged.Resolve("b");

		Assert.Multiple(() =>
		{
			Assert.That(a.LabelShadow, Is.True);
			Assert.That(a.LabelOutlineColor, Is.EqualTo("#222222"));
			Assert.That(a.LabelOutlineWidth, Is.EqualTo(2.5));
			Assert.That(a.LabelBoxBorderColor, Is.EqualTo("#444444"));
			Assert.That(a.LabelBoxBorderWidth, Is.EqualTo(1));
			Assert.That(b.LabelShadow, Is.False);
			Assert.That(b.LabelOutlineColor, Is.EqualTo("#111111"));
			Assert.That(b.LabelOutlineWidth, Is.EqualTo(2));
			Assert.That(b.LabelBoxBorderColor, Is.EqualTo("#333333"));
			Assert.That(b.LabelBoxBorderWidth, Is.EqualTo(3));
		});
	}

	[TestCase("""{"labelOutlineColor":"#111111"}""", true, 1d)]
	[TestCase("""{"labelOutlineColor":"#111111","labelOutlineWidth":0}""", false, 0d)]
	[TestCase("""{"labelOutlineWidth":3}""", false, 3d)]
	[TestCase("""{"labelOutlineColor":"#111111","labelOutlineWidth":40}""", true, 3d)]
	[TestCase("""{"labelOutlineColor":"#111111","labelOutlineWidth":-2}""", false, 0d)]
	public void A_label_outline_needs_a_colour_and_a_width_within_range(string data, bool outlined, double width)
	{
		var appearance = ActionButtonWidgetData.Parse(JsonDocument.Parse(data).RootElement).Resolve(null);

		Assert.Multiple(() =>
		{
			Assert.That(appearance.HasLabelOutline, Is.EqualTo(outlined));
			Assert.That(appearance.LabelOutlineWidth, Is.EqualTo(width));
		});
	}

	// An absent key advances: a button saved before the flag existed keeps cycling, and only an
	// explicit false turns it off.
	[TestCase("""{"stateMode":true,"states":[{"id":"a"},{"id":"b"}]}""", true)]
	[TestCase("""{"stateMode":true,"cycleStatesOnPress":true,"states":[{"id":"a"},{"id":"b"}]}""", true)]
	[TestCase("""{"stateMode":true,"cycleStatesOnPress":false,"states":[{"id":"a"},{"id":"b"}]}""", false)]
	[TestCase("""{"stateMode":false,"states":[{"id":"a"},{"id":"b"}]}""", false)]
	public void A_press_advances_the_state_unless_the_button_turned_cycling_off(string data, bool advances)
	{
		var config = ActionButtonWidgetData.Parse(JsonDocument.Parse(data).RootElement);

		Assert.Multiple(() =>
		{
			Assert.That(config.CanAdvanceState, Is.EqualTo(advances));

			// A button with no short-press flow declares "press" for exactly one reason: it cycles.
			Assert.That(config.DeclaredTriggers().Contains(MacroDeck.Ui.Components.UiComponentEvents.Press),
				Is.EqualTo(advances));
		});
	}

	[Test]
	public void A_button_governed_by_a_mapping_never_advances_on_a_press()
	{
		const string data = """
							{"stateMode":true,"states":[{"id":"a"},{"id":"b"}],
							 "stateMapping":{"rules":[{"id":"r1","stateId":"b","when":{}}],"fallbackStateId":"a"}}
							""";

		var config = ActionButtonWidgetData.Parse(JsonDocument.Parse(data).RootElement);

		Assert.That(config.CanAdvanceState, Is.False);
	}

	[Test]
	public void A_label_position_cascades_state_then_root_and_stays_within_the_button()
	{
		var config = Parse("""
						   {"stateMode":true,"labelX":12,"labelY":250,
						    "states":[{"id":"a","label":"A","appearance":{"labelX":40}},{"id":"b","label":"B"},
						              {"id":"c","label":"C","appearance":{"textAlign":"right"}}]}
						   """);

		Assert.Multiple(() =>
		{
			Assert.That(config.Resolve("a").LabelX, Is.EqualTo(40));
			Assert.That(config.Resolve("b").LabelX, Is.EqualTo(12));
			Assert.That(config.Resolve("c").LabelX, Is.EqualTo(100), "a state's alignment is its own position");
			Assert.That(config.Resolve("a").LabelY, Is.EqualTo(100));
			Assert.That(Parse("{}").Resolve(null).LabelX, Is.EqualTo(50));
		});
	}

	[TestCase("left", "top", 0, 0)]
	[TestCase("center", "center", 50, 50)]
	[TestCase("right", "bottom", 100, 100)]
	public void A_button_saved_with_an_alignment_sits_at_that_preset_position(
		string textAlign,
		string labelPosition,
		double x,
		double y)
	{
		var config = Parse($$"""{"label":"Main","textAlign":"{{textAlign}}","labelPosition":"{{labelPosition}}"}""");
		var appearance = config.Resolve(null);

		Assert.Multiple(() =>
		{
			Assert.That(appearance.LabelX, Is.EqualTo(x));
			Assert.That(appearance.LabelY, Is.EqualTo(y));
			Assert.That(appearance.TextAlign, Is.EqualTo(textAlign));
			Assert.That(appearance.LabelPosition, Is.EqualTo(labelPosition));
			Assert.That(config.UsesLabelLayer, Is.False);
		});
	}

	[Test]
	public void A_free_position_wins_over_a_stored_alignment_and_only_a_position_off_the_presets_needs_the_layer()
	{
		var onPreset = Parse("""{"label":"Main","textAlign":"center","labelX":0,"labelY":100}""");
		var free = Parse("""{"label":"Main","textAlign":"right","labelX":20}""");

		Assert.Multiple(() =>
		{
			Assert.That(onPreset.Resolve(null).TextAlign, Is.EqualTo("left"));
			Assert.That(onPreset.Resolve(null).LabelPosition, Is.EqualTo("bottom"));
			Assert.That(onPreset.UsesLabelLayer, Is.False);
			Assert.That(free.Resolve(null).LabelX, Is.EqualTo(20));
			Assert.That(free.UsesLabelLayer, Is.True);
		});
	}

	[Test]
	public void An_extra_label_takes_defaults_for_what_it_leaves_unset_and_never_the_primary_labels_values()
	{
		var config = Parse("""
						   {"label":"Main","labelColor":"#00ff00","fontSize":30,"textAlign":"left",
						    "extraLabels":[{"id":"a","label":"Extra","labelPosition":"bottom","labelX":7}]}
						   """);
		var extra = config.Resolve(null).ExtraLabels.Single();

		Assert.Multiple(() =>
		{
			Assert.That(extra.Text, Is.EqualTo("Extra"));
			Assert.That(extra.Color, Is.EqualTo("#ffffff"));
			Assert.That(extra.FontSizePercent, Is.EqualTo(14));
			Assert.That(extra.X, Is.EqualTo(7));
			Assert.That(extra.Y, Is.EqualTo(100));
			Assert.That(extra.Shadow, Is.True);
		});
	}

	[Test]
	public void A_button_shows_at_most_three_extra_labels_however_many_its_data_holds()
	{
		var config = Parse("""
						   {"extraLabels":[{"label":"1"},{"label":"2"},"not a label",{"label":"3"},{"label":"4"},{"label":"5"}]}
						   """);

		Assert.That(config.Resolve(null).ExtraLabels.Select(label => label.Text), Is.EqualTo(new[] { "1", "2", "3" }));
	}

	[Test]
	public void Each_state_owns_its_extra_labels_and_never_shows_the_buttons_or_another_states()
	{
		var config = Parse("""
						   {"stateMode":true,"extraLabels":[{"id":"r","label":"Button level"}],
						    "states":[{"id":"a","label":"A","appearance":{"extraLabels":[{"id":"x","label":"Only A"}]}},
						              {"id":"b","label":"B"}]}
						   """);

		Assert.Multiple(() =>
		{
			Assert.That(config.Resolve("a").ExtraLabels.Select(label => label.Text), Is.EqualTo(new[] { "Only A" }));
			Assert.That(config.Resolve("b").ExtraLabels, Is.Empty);
			Assert.That(config.ExtraLabelSlots, Is.EqualTo(1));
		});
	}

	[Test]
	public void Extra_labels_the_current_mode_never_shows_do_not_change_how_the_button_is_drawn()
	{
		var stateMode = Parse("""
							  {"stateMode":true,"extraLabels":[{"id":"r","label":"Button level"}],
							   "states":[{"id":"a","label":"A"},{"id":"b","label":"B"}]}
							  """);
		var plain = Parse("""
						  {"stateMode":false,"label":"Main",
						   "states":[{"id":"a","label":"A","appearance":{"extraLabels":[{"id":"x","label":"Dormant"}]}}]}
						  """);

		Assert.Multiple(() =>
		{
			Assert.That(stateMode.UsesLabelLayer, Is.False);
			Assert.That(plain.UsesLabelLayer, Is.False);
			Assert.That(plain.Resolve(null).ExtraLabels, Is.Empty);
		});
	}

	private static ActionButtonWidgetData Parse(string json)
	{
		return ActionButtonWidgetData.Parse(JsonDocument.Parse(json).RootElement);
	}
}
