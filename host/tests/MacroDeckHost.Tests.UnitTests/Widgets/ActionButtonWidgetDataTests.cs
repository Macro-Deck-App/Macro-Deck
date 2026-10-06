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
																	   """).RootElement);

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
		var legacy = ActionButtonWidgetData.Parse(JsonDocument.Parse("""{"label":"Old","labelColor":"#eeeeee"}""").RootElement);
		var staged = ActionButtonWidgetData.Parse(JsonDocument.Parse("""
			{"stateMode":true,"labelBoxColor":"#111111",
			 "states":[{"id":"a","appearance":{"labelBoxColor":"#222222"}},{"id":"b","appearance":{"label":"B"}}]}
			""").RootElement);

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
		var legacy = ActionButtonWidgetData.Parse(JsonDocument.Parse("""{"label":"Old","labelColor":"#eeeeee"}""").RootElement)
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
			""").RootElement);

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
}
