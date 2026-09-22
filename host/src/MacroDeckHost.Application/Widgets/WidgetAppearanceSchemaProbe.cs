using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Application.Widgets;

public sealed class WidgetAppearanceSchemaProbe
{
	private const int MaxEntries = 512;

	private static readonly string[] _sampleState = ["off"];

	private readonly ConcurrentDictionary<(string Type, string Data, WidgetAppearanceProperty Property),
		(JsonSchema Schema, bool Accepted)> _results = new();

	public bool Accepts(string type, JsonSchema schema, string data, WidgetAppearanceProperty property)
	{
		var key = (type, data, property);
		if (_results.TryGetValue(key, out var cached) && ReferenceEquals(cached.Schema, schema))
		{
			return cached.Accepted;
		}

		var accepted = Probe(schema, data, property);
		if (_results.Count >= MaxEntries)
		{
			_results.Clear();
		}

		_results[key] = (schema, accepted);
		return accepted;
	}

	public static bool AddsProblems(JsonSchema schema, JsonObject before, JsonObject after)
	{
		var known = Problems(schema, before);
		return Problems(schema, after).Any(problem => !known.Contains(problem));
	}

	private static bool Probe(JsonSchema schema, string data, WidgetAppearanceProperty property)
	{
		var before = WidgetAppearanceJson.ParseDataBag(data);
		var after = before.DeepClone().AsObject();
		WidgetAppearanceJson.Apply(after, string.Empty, Sample(property), _sampleState, [property]);
		return !AddsProblems(schema, before, after);
	}

	private static WidgetAppearancePatch Sample(WidgetAppearanceProperty property) => property switch
	{
		WidgetAppearanceProperty.BackgroundColor => new WidgetAppearancePatch { BackgroundColor = "#000000" },
		WidgetAppearanceProperty.Label => new WidgetAppearancePatch { Label = "Label" },
		WidgetAppearanceProperty.LabelColor => new WidgetAppearancePatch { LabelColor = "#000000" },
		WidgetAppearanceProperty.AccentColor => new WidgetAppearancePatch { AccentColor = "#000000" },
		WidgetAppearanceProperty.Font => new WidgetAppearancePatch
		{
			FontFaceId = "sample", FontSize = 12, TextAlign = "center", LabelPosition = "center"
		},
		_ => new WidgetAppearancePatch()
	};

	private static HashSet<(string Pointer, string Keyword)> Problems(JsonSchema schema, JsonObject data)
	{
		using var document = JsonDocument.Parse(data.ToJsonString());
		return WidgetDataSchema.Validate(schema, document.RootElement)
			.Select(problem => (problem.Pointer, problem.Keyword))
			.ToHashSet();
	}
}
