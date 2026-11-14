using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Application.Widgets;

public sealed record WidgetStyle(IReadOnlyList<string> Keys, JsonObject Values)
{
	public string ToJson()
	{
		var keys = new JsonArray();
		foreach (var key in Keys)
		{
			keys.Add(key);
		}

		return new JsonObject { ["keys"] = keys, ["values"] = Values.DeepClone() }.ToJsonString();
	}
}

public static class WidgetStyleJson
{
	public const string BackgroundColor = "backgroundColor";

	public const string Border = "border";

	private const string IconColor = "iconColor";

	private const string IconDisplay = "iconDisplay";

	private const int MaxKeys = 64;

	private static readonly string[] _iconKeys = [IconColor, IconDisplay];

	private static readonly string[] _objectKeys = [Border, IconDisplay];

	private static readonly string[] _defaultBuiltInKeys = [BackgroundColor, Border];

	private static readonly string[] _accentKeys = ["accentColor", BackgroundColor, Border];

	private static readonly Dictionary<string, string[]> _builtInKeys = new(StringComparer.Ordinal)
	{
		[WidgetTypeIds.ActionButton] =
		[
			BackgroundColor,
			"labelColor",
			"labelBoxColor",
			"labelShadow",
			"labelOutlineColor",
			"labelOutlineWidth",
			"labelBoxBorderColor",
			"labelBoxBorderWidth",
			"fontFaceId",
			"fontSize",
			"textAlign",
			"labelPosition",
			Border,
			IconColor,
			IconDisplay
		],
		[WidgetTypeIds.Slider] = ["color", "labelColor", BackgroundColor, IconColor, Border],
		[WidgetTypeIds.HistoryGraph] = _accentKeys,
		[WidgetTypeIds.Countdown] = _accentKeys,
		[WidgetTypeIds.Stopwatch] = _accentKeys,
		[WidgetTypeIds.Clock] = [BackgroundColor, "textColor", Border],
		[WidgetTypeIds.Battery] = ["color", "colorMode", "chargingColor", BackgroundColor, Border]
	};

	public static IReadOnlyList<string> ProviderKeys { get; } = [Border];

	public static IReadOnlyList<string> ProviderKeysWithBackground { get; } = [BackgroundColor, Border];

	public static IReadOnlyList<string>? BuiltInKeys(string type)
	{
		if (_builtInKeys.TryGetValue(type, out var keys))
		{
			return keys;
		}

		return WidgetTypeIds.BuiltIn.Contains(type) ? _defaultBuiltInKeys : null;
	}

	public static bool TryParse(string? json, out WidgetStyle style)
	{
		style = new WidgetStyle([], []);
		if (string.IsNullOrWhiteSpace(json))
		{
			return false;
		}

		JsonObject? root;
		try
		{
			root = JsonNode.Parse(json) as JsonObject;
		}
		catch (JsonException)
		{
			return false;
		}

		if (root?["keys"] is not JsonArray keyNodes
			|| root["values"] is not JsonObject values
			|| keyNodes.Count is 0 or > MaxKeys)
		{
			return false;
		}

		var keys = new List<string>(keyNodes.Count);
		foreach (var node in keyNodes)
		{
			if (node is not JsonValue value || !value.TryGetValue<string>(out var key) || key.Length == 0)
			{
				return false;
			}

			keys.Add(key);
		}

		foreach (var (key, value) in values)
		{
			var valid = _objectKeys.Contains(key) ? value is JsonObject : value is JsonValue;
			if (!valid || !keys.Contains(key))
			{
				return false;
			}
		}

		style = new WidgetStyle(keys, values.DeepClone().AsObject());
		return true;
	}

	public static WidgetStyle Extract(string type, JsonObject data, string? shownStateId, IReadOnlyList<string> keys)
	{
		var values = new JsonObject();
		var isButton = type == WidgetTypeIds.ActionButton;
		var stateMode = isButton && ActionButtonStateModel.Read(data).StateMode;
		var shownState = stateMode ? FindAppearance(data, shownStateId) : null;
		var iconSource = !isButton ? data : stateMode ? shownState : MomentaryIconSource(data);

		foreach (var key in keys)
		{
			JsonNode? value;
			if (isButton && _iconKeys.Contains(key))
			{
				value = iconSource?[key];
			}
			else
			{
				value = IsSet(shownState?[key]) ? shownState![key] : data[key];
			}

			if (IsSet(value))
			{
				values[key] = value!.DeepClone();
			}
		}

		return new WidgetStyle(keys, values);
	}

	public static bool Apply(string type, JsonObject data, WidgetStyle style, IReadOnlyList<string> targetKeys)
	{
		var keys = targetKeys.Where(style.Keys.Contains).ToList();
		if (type != WidgetTypeIds.ActionButton)
		{
			return Write(data, keys, style.Values);
		}

		var stateMode = ActionButtonStateModel.Read(data).StateMode;
		var changed = !stateMode
			&& keys.Exists(_iconKeys.Contains)
			&& WidgetAppearanceJson.HoistLegacyMomentaryIcon(data);

		var lookKeys = keys.Where(key => !_iconKeys.Contains(key)).ToList();
		changed |= Write(data, stateMode ? lookKeys : keys, style.Values);

		var stateKeys = stateMode ? keys : lookKeys;
		foreach (var entry in StateEntries(data))
		{
			if (entry["appearance"] is not JsonObject appearance)
			{
				appearance = [];
				entry["appearance"] = appearance;
			}

			changed |= Write(appearance, stateKeys, style.Values);
		}

		return changed;
	}

	private static bool Write(JsonObject target, IReadOnlyList<string> keys, JsonObject values)
	{
		var changed = false;
		foreach (var key in keys)
		{
			var value = values[key];
			if (value is null)
			{
				changed |= target.Remove(key);
				continue;
			}

			if (target[key] is { } current && JsonNode.DeepEquals(current, value))
			{
				continue;
			}

			target[key] = value.DeepClone();
			changed = true;
		}

		return changed;
	}

	private static List<JsonObject> StateEntries(JsonObject data)
	{
		var states = (data["states"] as JsonArray ?? []).OfType<JsonObject>();
		var backup = ((data["manualStateBackup"] as JsonObject)?["states"] as JsonArray ?? []).OfType<JsonObject>();
		return states.Concat(backup).ToList();
	}

	private static JsonObject? FindAppearance(JsonObject data, string? stateId)
	{
		var entry = (data["states"] as JsonArray ?? [])
			.OfType<JsonObject>()
			.FirstOrDefault(state => state["id"] is JsonValue id
				&& id.TryGetValue<string>(out var text)
				&& string.Equals(text, stateId, StringComparison.Ordinal));
		return entry?["appearance"] as JsonObject;
	}

	// A momentary button saved before its icon moved to the root still keeps it on its first state,
	// and that copy is the one shown while the root holds no icon.
	private static JsonObject? MomentaryIconSource(JsonObject data)
	{
		if (data["icon"] is not null || data["iconId"] is not null || data[IconDisplay] is not null)
		{
			return data;
		}

		var first = (data["states"] as JsonArray ?? []).OfType<JsonObject>().FirstOrDefault();
		return first?["appearance"] as JsonObject ?? data;
	}

	private static bool IsSet(JsonNode? node)
	{
		return node is not null
			&& !(node is JsonValue value && value.TryGetValue<string>(out var text) && string.IsNullOrWhiteSpace(text));
	}
}
