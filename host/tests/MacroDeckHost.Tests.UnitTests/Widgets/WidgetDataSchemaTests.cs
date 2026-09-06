using System.Text.Json;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Widgets;

[TestFixture]
public class WidgetDataSchemaTests
{
	private static readonly WidgetDataSchemaProvider _provider = new(new WidgetTypeRegistry(new RecordingMediator()));

	private static IReadOnlyList<WidgetDataProblem> Validate(string type, string json)
	{
		if (!_provider.TryGet(type, out var schema))
		{
			throw new AssertionException($"no schema registered for {type}");
		}

		using var document = JsonDocument.Parse(json);
		return WidgetDataSchema.Validate(schema, document.RootElement);
	}

	private static string Describe(IReadOnlyList<WidgetDataProblem> problems)
		=> string.Join("; ", problems.Select(p => $"{p.Pointer}:{p.Keyword}:{p.Message}"));

	[Test]
	public void Every_widget_type_has_a_schema_and_an_empty_object_validates()
	{
		foreach (var type in WidgetTypeIds.BuiltIn)
		{
			var problems = Validate(type, "{}");
			Assert.That(problems, Is.Empty, $"{type}: {{}} must validate; got {Describe(problems)}");
		}

		var distinctSchemas = _provider.All().Values.Select(e => e.GetRawText()).Distinct().Count();
		Assert.That(distinctSchemas,
			Is.GreaterThan(1),
			"widget types must not all resolve to the same schema document");
	}

	[TestCase("""{"label":"x","imageUrl":"http://example/x.png","backgroundColor":"#666"}""",
		TestName = "LegacyActionButton_NoModeNoStates")]
	[TestCase(
		"""{"mode":"toggle","offState":{"label":"off"},"onState":{"label":"on"},"states":{"off":{"label":"off"},"on":{"label":"on"}}}""",
		TestName = "ToggleButton_BothLegacyMirrorsAndNewStateShape")]
	[TestCase("""{"someFuturePluginOrClientKey":42}""", TestName = "UnknownProperty")]
	[TestCase("""{"textAlign":null}""", TestName = "NullEnum_TextAlign")]
	[TestCase("""{"border":{"style":null}}""", TestName = "NullEnum_BorderStyle")]
	[TestCase("""{"fontFamily":"Roboto","fontBold":true,"fontItalic":false}""",
		TestName = "LegacyFontFields_StillValidate")]
	[TestCase("""{"mode":"toggle","states":{"off":{"fontFamily":"Roboto","fontBold":false},"on":{}}}""",
		TestName = "LegacyStateFontFields_StillValidate")]
	[TestCase("""{"fontFaceId":"roboto-700-5-upright"}""", TestName = "FontFaceId_Validates")]
	[TestCase("""{"icon":{"type":"icon-pack","reference":"0198aaaa-1111-2222-3333-444444444444"}}""",
		TestName = "TypedIcon_IconPackReference_Validates")]
	[TestCase("""{"icon":{"type":"plugin-asset","reference":"spotify:album/4aawyAB9vmqN3uQ7FjRGTy"}}""",
		TestName = "TypedIcon_NonGuidReference_Validates")]
	[TestCase("""{"icon":{"type":"future-provider","reference":"x"}}""",
		TestName = "TypedIcon_UnknownProviderType_StillValidates_NoClosedEnum")]
	[TestCase(
		"""{"stateMode":true,"states":[{"id":"off","label":"Off","appearance":{"icon":{"type":"icon-pack","reference":"0198aaaa-1111-2222-3333-444444444444"}}},{"id":"on","label":"On","appearance":{}}]}""",
		TestName = "TypedIcon_OnAPerStateAppearance_Validates")]
	public void ActionButton_data_that_saves_today_still_validates(string json)
	{
		var problems = Validate(WidgetTypeIds.ActionButton, json);
		Assert.That(problems, Is.Empty, Describe(problems));
	}

	[TestCase(WidgetTypeIds.ActionButton)]
	[TestCase(WidgetTypeIds.MusicPlayer)]
	[TestCase(WidgetTypeIds.Weather)]
	[TestCase(WidgetTypeIds.HistoryGraph)]
	[TestCase(WidgetTypeIds.Clock)]
	public void Flows_as_a_nested_JSON_string_validates(string type)
	{
		var problems = Validate(type, """{"flows":"[{\"triggerType\":\"onShortPress\",\"children\":[]}]"}""");
		Assert.That(problems, Is.Empty, Describe(problems));
	}

	[Test]
	public void ActionButton_states_missing_on_is_invalid_with_a_pointer_at_states()
	{
		Assert.That(Validate(WidgetTypeIds.ActionButton, "{}"), Is.Empty, "an empty object must always validate");

		var problems = Validate(WidgetTypeIds.ActionButton, """{"states":{"off":{}}}""");

		Assert.Multiple(() =>
		{
			Assert.That(problems, Is.Not.Empty, "states without 'on' must be rejected");
			Assert.That(problems,
				Has.Some.Matches<WidgetDataProblem>(p => p.Pointer == "/states"),
				$"expected a problem pointing at /states; got {Describe(problems)}");
		});

		Assert.That(Validate(WidgetTypeIds.ActionButton, """{"states":{"off":{},"on":{}}}"""), Is.Empty);
	}

	[TestCase(
		"""{"mode":"momentary","iconId":"i","iconDisplay":{"fit":"cover","zoom":250,"offsetX":-20,"offsetY":10,"opacity":40}}""",
		true)]
	[TestCase("""{"mode":"toggle","states":{"off":{"iconId":"a"},"on":{"iconId":"b"}}}""", true)]
	[TestCase(
		"""{"mode":"momentary","iconId":"i","states":{"off":{"backgroundColor":"#111"},"on":{"backgroundColor":"#222"}}}""",
		true)]
	[TestCase("""{"mode":"toggle","offState":{"label":"off"},"onState":{"label":"on"}}""", true)]
	[TestCase("""{"iconDisplay":{"zoom":"big"}}""", false)]
	public void RootIconId_and_iconDisplay_validate_as_documented(string json, bool expectedValid)
	{
		var problems = Validate(WidgetTypeIds.ActionButton, json);
		Assert.That(problems,
			expectedValid ? Is.Empty : Is.Not.Empty,
			expectedValid ? Describe(problems) : "expected a schema violation but none was reported");
	}

	[TestCase(WidgetTypeIds.Clock, """{"style":"sundial"}""", false)]
	[TestCase(WidgetTypeIds.Clock, """{"style":"analog"}""", true)]
	[TestCase(WidgetTypeIds.Clock, """{"showSeconds":"yes"}""", false)]
	[TestCase(WidgetTypeIds.Clock, """{"showSeconds":true}""", true)]
	[TestCase(WidgetTypeIds.Slider, """{"orientation":"diagonal"}""", false)]
	[TestCase(WidgetTypeIds.Slider, """{"orientation":"vertical"}""", true)]
	[TestCase(WidgetTypeIds.Weather, """{"forecastDays":"five"}""", false)]
	[TestCase(WidgetTypeIds.Weather, """{"forecastDays":5}""", true)]
	public void Wrong_types_and_bad_enums_are_rejected_per_type(string type, string json, bool expectedValid)
	{
		var problems = Validate(type, json);
		Assert.That(problems,
			expectedValid ? Is.Empty : Is.Not.Empty,
			expectedValid ? Describe(problems) : "expected a schema violation but none was reported");
	}

	[TestCase("""{"icon":{"type":"icon-pack","reference":"0198bbbb-1111-2222-3333-444444444444"}}""", true)]
	[TestCase("""{"icon":{"type":"plugin-asset","reference":"spotify:album/4aawyAB9vmqN3uQ7FjRGTy"}}""", true)]
	[TestCase("""{"iconId":"0198bbbb-1111-2222-3333-444444444444"}""", true)]
	public void Slider_icon_validates_including_a_non_GUID_reference_and_the_legacy_iconId(string json,
		bool expectedValid)
	{
		var problems = Validate(WidgetTypeIds.Slider, json);
		Assert.That(problems,
			expectedValid ? Is.Empty : Is.Not.Empty,
			expectedValid ? Describe(problems) : "expected a schema violation but none was reported");
	}

	[Test]
	public void Every_embedded_schema_uses_only_allowlisted_keywords()
	{
		var allowlist = new HashSet<string>
		{
			"$schema", "$id", "$ref", "$defs", "$comment", "title", "description", "deprecated",
			"type", "properties", "items", "enum", "required", "anyOf", "additionalProperties",
			"minimum", "maximum"
		};

		foreach (var (name, element) in _provider.All())
		{
			var offending = new List<string>();
			CollectDisallowedKeywords(element, allowlist, offending);
			Assert.That(offending, Is.Empty, $"{name} uses disallowed keyword(s): {string.Join(", ", offending)}");
		}
	}

	private static void CollectDisallowedKeywords(JsonElement schema, HashSet<string> allowlist, List<string> offending)
	{
		if (schema.ValueKind != JsonValueKind.Object)
		{
			return;
		}

		foreach (var property in schema.EnumerateObject())
		{
			if (!allowlist.Contains(property.Name))
			{
				offending.Add(property.Name);
			}

			switch (property.Name)
			{
				case "properties" or "$defs" when property.Value.ValueKind == JsonValueKind.Object:
					foreach (var nested in property.Value.EnumerateObject())
					{
						CollectDisallowedKeywords(nested.Value, allowlist, offending);
					}

					break;
				case "items":
					CollectDisallowedKeywords(property.Value, allowlist, offending);
					break;
				case "anyOf" when property.Value.ValueKind == JsonValueKind.Array:
					foreach (var sub in property.Value.EnumerateArray())
					{
						CollectDisallowedKeywords(sub, allowlist, offending);
					}

					break;
			}
		}
	}
}
