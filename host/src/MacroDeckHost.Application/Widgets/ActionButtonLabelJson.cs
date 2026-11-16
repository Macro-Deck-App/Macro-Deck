using System.Text.Json.Nodes;

namespace MacroDeckHost.Application.Widgets;

public static class ActionButtonLabelJson
{
	public const string ExtraLabelsKey = "extraLabels";
	public const string XKey = "labelX";
	public const string YKey = "labelY";
	public const double Start = 0;
	public const double Center = 50;
	public const double End = 100;
	public const int MaxLabels = 4;
	public const int MaxExtraLabels = MaxLabels - 1;

	// A label id is part of composed node ids, which are capped: a longer one would not fit under a state.
	private const int MaxIdLength = 12;

	public static void Normalize(JsonObject data)
	{
		NormalizeList(data);

		if (data["states"] is not JsonArray states)
		{
			return;
		}

		foreach (var state in states.OfType<JsonObject>())
		{
			if (state["appearance"] is JsonObject appearance)
			{
				NormalizeList(appearance);
			}
		}
	}

	public static IReadOnlyList<JsonObject> ReadExtraLabels(JsonObject? target)
	{
		return target?[ExtraLabelsKey] is JsonArray labels
			? labels.OfType<JsonObject>().Take(MaxExtraLabels).ToList()
			: [];
	}

	public static IReadOnlyList<JsonObject> ActiveExtraLabels(ActionButtonStateModel model, string? stateId)
	{
		ArgumentNullException.ThrowIfNull(model);

		return ReadExtraLabels(model.StateMode ? model.FindState(stateId)?.Appearance : model.Data);
	}

	public static int ExtraLabelSlots(ActionButtonStateModel model)
	{
		ArgumentNullException.ThrowIfNull(model);

		if (!model.StateMode)
		{
			return ReadExtraLabels(model.Data).Count;
		}

		return model.States.Count == 0 ? 0 : model.States.Max(state => ReadExtraLabels(state.Appearance).Count);
	}

	public static double? ReadX(JsonObject? target)
	{
		return ReadPosition(target, XKey)
			?? ReadString(target, "textAlign") switch
			{
				"left" => Start,
				"center" => Center,
				"right" => End,
				_ => null
			};
	}

	public static double? ReadY(JsonObject? target)
	{
		return ReadPosition(target, YKey)
			?? ReadString(target, "labelPosition") switch
			{
				"top" => Start,
				"center" => Center,
				"bottom" => End,
				_ => null
			};
	}

	public static double? ReadPosition(JsonObject? target, string key)
	{
		return target?[key] is JsonValue value
			&& value.TryGetValue<double>(out var position)
			&& double.IsFinite(position)
				? Math.Clamp(position, Start, End)
				: null;
	}

	public static bool IsPreset(double position)
	{
		return position is Start or Center or End;
	}

	public static string TextAlignOf(double x)
	{
		return Third(x) switch
		{
			< 0 => "left",
			> 0 => "right",
			_ => "center"
		};
	}

	public static string LabelPositionOf(double y)
	{
		return Third(y) switch
		{
			< 0 => "top",
			> 0 => "bottom",
			_ => "center"
		};
	}

	private static int Third(double position)
	{
		return position < 100.0 / 3 ? -1 : position > 200.0 / 3 ? 1 : 0;
	}

	private static string? ReadString(JsonObject? target, string key)
	{
		return target?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
	}

	public static void NormalizeList(JsonObject target)
	{
		if (target[ExtraLabelsKey] is not JsonArray labels)
		{
			return;
		}

		var seenIds = new HashSet<string>(StringComparer.Ordinal);
		var normalized = new JsonArray();
		foreach (var label in labels.OfType<JsonObject>().Take(MaxExtraLabels))
		{
			var candidate = label["id"] is JsonValue idValue
				&& idValue.TryGetValue<string>(out var raw)
				&& raw.Length <= MaxIdLength
				&& ActionButtonStateJson.IsValidId(raw)
					? raw
					: null;
			var clone = (JsonObject)label.DeepClone();
			clone["id"] = candidate is not null && seenIds.Add(candidate) ? candidate : GenerateUniqueId(seenIds);
			normalized.Add(clone);
		}

		target[ExtraLabelsKey] = normalized;
	}

	private static string GenerateUniqueId(HashSet<string> seenIds)
	{
		string id;
		do
		{
			id = NewId();
		} while (!seenIds.Add(id));

		return id;
	}

	public static string NewId()
	{
		return $"l{Guid.NewGuid():N}"[..MaxIdLength];
	}
}
