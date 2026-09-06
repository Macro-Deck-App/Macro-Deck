using System.Text.Json.Nodes;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Widgets;
using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Widgets;

[TestFixture]
public class WidgetAppearanceJsonTests
{
	private static readonly string[] _offThenOn = ["off", "on"];
	private static readonly string[] _offOnly = ["off"];

	[Test]
	public void ToggleButton_WritesTheSelectedState()
	{
		var data = Parse(
			"""{"stateMode":true,"states":[{"id":"off","label":"Off","appearance":{}},{"id":"on","label":"On","appearance":{}}]}""");

		var changed = WidgetAppearanceJson.Apply(data,
			WidgetTypeIds.ActionButton,
			new WidgetAppearancePatch { Label = "Live" },
			["on"]);

		Assert.Multiple(() =>
		{
			Assert.That(changed, Is.True);
			Assert.That(Appearance(data, "on")!["label"]!.GetValue<string>(), Is.EqualTo("Live"));
			Assert.That(Appearance(data, "off")!["label"], Is.Null);
		});
	}

	[Test]
	public void ToggleButton_RestylingAState_LeavesTheLegacyAliasUntouched()
	{
		// A stray offState alias that happens to coexist with the current array shape (defensive: the
		// state-mode branch only ever touches states[i].appearance, never a legacy alias key).
		var data = Parse(
			"""{"stateMode":true,"states":[{"id":"off","label":"Off","appearance":{}},{"id":"on","label":"On","appearance":{}}],"offState":{"label":"stale"}}""");

		WidgetAppearanceJson.Apply(data,
			WidgetTypeIds.ActionButton,
			new WidgetAppearancePatch { Label = "fresh" },
			["off"]);

		Assert.Multiple(() =>
		{
			Assert.That(Appearance(data, "off")!["label"]!.GetValue<string>(), Is.EqualTo("fresh"));
			Assert.That(data["offState"]!["label"]!.GetValue<string>(), Is.EqualTo("stale"));
		});
	}

	[Test]
	public void MomentaryButton_WritesTheFlatFields()
	{
		var data = Parse("""{"mode":"momentary"}""");

		WidgetAppearanceJson.Apply(data,
			WidgetTypeIds.ActionButton,
			new WidgetAppearancePatch { Label = "Go", BackgroundColor = "#123456" },
			["off"]);

		Assert.Multiple(() =>
		{
			Assert.That(data["label"]!.GetValue<string>(), Is.EqualTo("Go"));
			Assert.That(data["backgroundColor"]!.GetValue<string>(), Is.EqualTo("#123456"));
			Assert.That(data["states"], Is.Null);
		});
	}

	[Test]
	public void MomentaryButton_WritesTheIconAndFramingFlat_WithoutCreatingStates()
	{
		var data = Parse("""{"mode":"momentary"}""");

		var changed = WidgetAppearanceJson.Apply(data,
			WidgetTypeIds.ActionButton,
			new WidgetAppearancePatch { IconId = "icon-1", IconZoom = 250 },
			["off"]);

		Assert.Multiple(() =>
		{
			Assert.That(changed, Is.True);
			Assert.That(data["icon"]!["type"]!.GetValue<string>(), Is.EqualTo("icon-pack"));
			Assert.That(data["icon"]!["reference"]!.GetValue<string>(), Is.EqualTo("icon-1"));
			Assert.That(data.ContainsKey("iconId"), Is.False, "writing always emits icon and removes iconId");
			Assert.That(data["iconDisplay"]!["zoom"]!.GetValue<double>(), Is.EqualTo(250));
			Assert.That(data["states"], Is.Null);
		});
	}

	[Test]
	public void MomentaryButton_RestylingTheIcon_HoistsAnyLegacyIconOffTheOffState()
	{
		// A leftover array-shaped states entry (e.g. left behind by disabling State Mode) still carries
		// an icon on its appearance; restyling a momentary button's icon hoists it out.
		var data = Parse("""{"states":[{"id":"off","label":"Off","appearance":{"iconId":"icon-old"}}]}""");

		WidgetAppearanceJson.Apply(data,
			WidgetTypeIds.ActionButton,
			new WidgetAppearancePatch { IconId = "icon-new" },
			["off"]);

		Assert.Multiple(() =>
		{
			Assert.That(data["icon"]!["reference"]!.GetValue<string>(), Is.EqualTo("icon-new"));
			Assert.That(Appearance(data, "off")!.ContainsKey("iconId"), Is.False);
			Assert.That(Appearance(data, "off")!.ContainsKey("icon"), Is.False);
		});
	}

	[Test]
	public void ToggleButton_WritesTheIconFramingOntoTheSelectedState()
	{
		var data = Parse(
			"""{"stateMode":true,"states":[{"id":"off","label":"Off","appearance":{}},{"id":"on","label":"On","appearance":{}}]}""");

		var changed = WidgetAppearanceJson.Apply(data,
			WidgetTypeIds.ActionButton,
			new WidgetAppearancePatch { IconFit = "cover", IconZoom = 250, IconOpacity = 40 },
			["on"]);

		Assert.Multiple(() =>
		{
			Assert.That(changed, Is.True);
			Assert.That(Appearance(data, "on")!["iconDisplay"]!["fit"]!.GetValue<string>(), Is.EqualTo("cover"));
			Assert.That(Appearance(data, "on")!["iconDisplay"]!["zoom"]!.GetValue<double>(), Is.EqualTo(250));
			Assert.That(Appearance(data, "on")!["iconDisplay"]!["opacity"]!.GetValue<double>(), Is.EqualTo(40));
			Assert.That(Appearance(data, "off")!["iconDisplay"], Is.Null);
			Assert.That(data["onState"], Is.Null);
		});
	}

	[Test]
	public void IconFraming_LeavesTheFieldsThePatchDoesNotCarry()
	{
		var data = Parse(
			"""{"stateMode":true,"states":[{"id":"off","label":"Off","appearance":{"iconDisplay":{"fit":"cover","zoom":200}}},{"id":"on","label":"On","appearance":{}}]}""");

		WidgetAppearanceJson.Apply(data,
			WidgetTypeIds.ActionButton,
			new WidgetAppearancePatch { IconOpacity = 50 },
			["off"]);

		Assert.Multiple(() =>
		{
			Assert.That(Appearance(data, "off")!["iconDisplay"]!["fit"]!.GetValue<string>(), Is.EqualTo("cover"));
			Assert.That(Appearance(data, "off")!["iconDisplay"]!["zoom"]!.GetValue<double>(), Is.EqualTo(200));
			Assert.That(Appearance(data, "off")!["iconDisplay"]!["opacity"]!.GetValue<double>(), Is.EqualTo(50));
		});
	}

	[Test]
	public void IconFraming_IsDroppedForATypeThatDoesNotRenderIt()
	{
		var data = Parse("""{"iconId":"icon-1"}""");

		var changed = WidgetAppearanceJson.Apply(data,
			WidgetTypeIds.Slider,
			new WidgetAppearancePatch { IconFit = "cover" },
			["off"]);

		Assert.Multiple(() =>
		{
			Assert.That(changed, Is.False);
			Assert.That(data["iconDisplay"], Is.Null);
		});
	}

	[Test]
	public void ClearingTheIconFraming_RemovesTheWholeObject()
	{
		var toggle = Parse(
			"""{"stateMode":true,"states":[{"id":"off","label":"Off","appearance":{}},{"id":"on","label":"On","appearance":{"iconDisplay":{"fit":"cover"}}}]}""");
		var momentary = Parse("""{"states":[{"id":"off","label":"Off","appearance":{"iconDisplay":{"zoom":300}}}]}""");

		var toggleCleared = WidgetAppearanceJson.ClearProperty(toggle,
			WidgetTypeIds.ActionButton,
			WidgetAppearanceProperty.IconDisplay,
			"on");
		var momentaryCleared = WidgetAppearanceJson.ClearProperty(momentary,
			WidgetTypeIds.ActionButton,
			WidgetAppearanceProperty.IconDisplay,
			"off");

		Assert.Multiple(() =>
		{
			Assert.That(toggleCleared, Is.True);
			Assert.That(Appearance(toggle, "on")!["iconDisplay"], Is.Null);
			Assert.That(momentaryCleared, Is.True);
			Assert.That(Appearance(momentary, "off")!["iconDisplay"], Is.Null);
		});
	}

	[Test]
	public void MomentaryButton_ClearingIconOrIconDisplay_RemovesTheRootKey()
	{
		var forIcon = Parse("""{"mode":"momentary","iconId":"i","iconDisplay":{"zoom":300}}""");
		var forDisplay = Parse("""{"mode":"momentary","iconId":"i","iconDisplay":{"zoom":300}}""");

		var iconCleared = WidgetAppearanceJson.ClearProperty(forIcon,
			WidgetTypeIds.ActionButton,
			WidgetAppearanceProperty.Icon,
			"off");
		var displayCleared = WidgetAppearanceJson.ClearProperty(forDisplay,
			WidgetTypeIds.ActionButton,
			WidgetAppearanceProperty.IconDisplay,
			"off");

		Assert.Multiple(() =>
		{
			Assert.That(iconCleared, Is.True);
			Assert.That(forIcon["iconId"], Is.Null);
			Assert.That(displayCleared, Is.True);
			Assert.That(forDisplay["iconDisplay"], Is.Null);
		});
	}

	[Test]
	public void MomentaryButton_ClearingIcon_AlsoRemovesTheLegacyOffStateCopy()
	{
		var data = Parse("""{"states":[{"id":"off","label":"Off","appearance":{"iconId":"i"}}]}""");

		var cleared = WidgetAppearanceJson.ClearProperty(data,
			WidgetTypeIds.ActionButton,
			WidgetAppearanceProperty.Icon,
			"off");

		Assert.Multiple(() =>
		{
			Assert.That(cleared, Is.True);
			Assert.That(data["iconId"], Is.Null);
			Assert.That(Appearance(data, "off")!.ContainsKey("iconId"), Is.False);
		});
	}

	[Test]
	public void NoMode_DefaultsToMomentaryBehaviour()
	{
		var data = Parse("{}");

		var changed = WidgetAppearanceJson.Apply(data,
			WidgetTypeIds.ActionButton,
			new WidgetAppearancePatch { Label = "Go", IconId = "i" },
			["off"]);

		Assert.Multiple(() =>
		{
			Assert.That(changed, Is.True);
			Assert.That(data["states"], Is.Null);
			Assert.That(data["label"]!.GetValue<string>(), Is.EqualTo("Go"));
			Assert.That(data["icon"]!["reference"]!.GetValue<string>(), Is.EqualTo("i"));
		});
	}

	[Test]
	public void Border_IsWrittenAsANestedObject()
	{
		var data = Parse("""{"mode":"momentary"}""");

		WidgetAppearanceJson.Apply(data,
			WidgetTypeIds.Clock,
			new WidgetAppearancePatch { BorderStyle = "blink", BorderColor = "#ff0000" },
			["off"]);

		Assert.Multiple(() =>
		{
			Assert.That(data["border"]!["style"]!.GetValue<string>(), Is.EqualTo("blink"));
			Assert.That(data["border"]!["color"]!.GetValue<string>(), Is.EqualTo("#ff0000"));
		});
	}

	[Test]
	public void PropertyTheTypeDoesNotRender_IsDropped()
	{
		var data = Parse("{}");

		var changed = WidgetAppearanceJson.Apply(data,
			WidgetTypeIds.Clock,
			new WidgetAppearancePatch { Label = "nope" },
			["off"]);

		Assert.Multiple(() =>
		{
			Assert.That(changed, Is.False);
			Assert.That(data["label"], Is.Null);
		});
	}

	[Test]
	public void HistoryGraph_TakesItsLabelAsTheCardTitle()
	{
		var data = Parse("{}");

		WidgetAppearanceJson.Apply(data,
			WidgetTypeIds.HistoryGraph,
			new WidgetAppearancePatch { Label = "CPU" },
			["off"]);

		Assert.That(data["title"]!.GetValue<string>(), Is.EqualTo("CPU"));
	}

	[Test]
	public void EmptyString_ClearsTheProperty()
	{
		var data = Parse("""{"mode":"momentary","label":"old"}""");

		WidgetAppearanceJson.Apply(data,
			WidgetTypeIds.ActionButton,
			new WidgetAppearancePatch { Label = string.Empty },
			["off"]);

		Assert.That(data["label"], Is.Null);
	}

	[Test]
	public void Font_StoresTheFaceId()
	{
		var data = Parse("""{"mode":"momentary"}""");

		var changed = WidgetAppearanceJson.Apply(data,
			WidgetTypeIds.ActionButton,
			new WidgetAppearancePatch { FontFaceId = "roboto-700-5-upright" },
			["off"]);

		Assert.Multiple(() =>
		{
			Assert.That(changed, Is.True);
			Assert.That(data["fontFaceId"]!.GetValue<string>(), Is.EqualTo("roboto-700-5-upright"));
		});
	}

	[Test]
	public void Font_EveryFontFieldNull_LeavesTheStoredFontUntouched()
	{
		var data = Parse("""{"mode":"momentary","fontFaceId":"existing-400-5-upright","fontSize":20}""");

		WidgetAppearanceJson.Apply(data,
			WidgetTypeIds.ActionButton,
			new WidgetAppearancePatch { Label = "Go" },
			["off"]);

		Assert.Multiple(() =>
		{
			Assert.That(data["fontFaceId"]!.GetValue<string>(), Is.EqualTo("existing-400-5-upright"));
			Assert.That(data["fontSize"]!.GetValue<double>(), Is.EqualTo(20));
		});
	}

	[Test]
	public void WritingTheValueItAlreadyHas_ReportsNoChange()
	{
		var data = Parse("""{"mode":"momentary","label":"same"}""");

		var changed = WidgetAppearanceJson.Apply(data,
			WidgetTypeIds.ActionButton,
			new WidgetAppearancePatch { Label = "same" },
			["off"]);

		Assert.That(changed, Is.False);
	}

	// Exercises the pre-existing selector-based resolver, not yet migrated to state ids.
#pragma warning disable CS0618
	[TestCase(true, WidgetStateSelector.Current, "on")]
	[TestCase(false, WidgetStateSelector.Current, "off")]
	[TestCase(false, WidgetStateSelector.On, "on")]
	[TestCase(true, WidgetStateSelector.Off, "off")]
	public void ToggleButton_ResolvesTheSelectorAgainstTheShownState(
		bool toggled,
		WidgetStateSelector selector,
		string expected)
	{
		var data = Parse($$"""{"mode":"toggle","isToggled":{{(toggled ? "true" : "false")}}}""");

		Assert.That(WidgetAppearanceJson.ResolveStates(data, WidgetTypeIds.ActionButton, ToStateIds(selector)),
			Is.EqualTo(new[] { expected }));
	}

	[Test]
	public void ToggleButton_Both_ResolvesToTheTwoStates()
	{
		var data = Parse("""{"mode":"toggle"}""");

		Assert.That(
			WidgetAppearanceJson.ResolveStates(data, WidgetTypeIds.ActionButton, ToStateIds(WidgetStateSelector.Both)),
			Is.EqualTo(_offThenOn));
	}

	[TestCase(WidgetStateSelector.On)]
	[TestCase(WidgetStateSelector.Both)]
	[TestCase(WidgetStateSelector.Current)]
	public void SingleStateWidget_CollapsesEverySelector(WidgetStateSelector selector)
	{
		var data = Parse("{}");

		Assert.Multiple(() =>
		{
			Assert.That(WidgetAppearanceJson.ResolveStates(data, WidgetTypeIds.Slider, ToStateIds(selector)),
				Is.EqualTo(_offOnly));
			Assert.That(WidgetAppearanceJson.ResolveStates(data, WidgetTypeIds.ActionButton, ToStateIds(selector)),
				Is.EqualTo(_offOnly));
		});
	}

	private static IReadOnlyCollection<string> ToStateIds(WidgetStateSelector selector) => selector switch
	{
		WidgetStateSelector.On => ["on"],
		WidgetStateSelector.Off => ["off"],
		WidgetStateSelector.Both => [WidgetStates.All],
		_ => [WidgetStates.Current]
	};
#pragma warning restore CS0618

	private static JsonObject Parse(string json) => WidgetAppearanceJson.ParseDataBag(json);

	/// <summary>Looks up a state's appearance object by id in the current array shape.</summary>
	private static JsonObject? Appearance(JsonObject data, string stateId)
		=> data["states"]!.AsArray()
			.OfType<JsonObject>()
			.FirstOrDefault(entry => entry["id"]!.GetValue<string>() == stateId)?["appearance"]
			?.AsObject();
}
