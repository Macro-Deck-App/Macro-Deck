using System.Text.Json;

namespace MacroDeckHost.Rendering.Tree;

internal sealed class UiNode
{
	private static readonly IReadOnlyList<UiNode> _noChildren = [];

	public required string Id { get; init; }

	public required string Type { get; init; }

	public int RequiredComponentVersion { get; init; } = 1;

	public JsonElement? Properties { get; init; }

	public IReadOnlyList<UiNode> Children { get; init; } = _noChildren;

	public UiNode? Fallback { get; init; }

	public static UiNode? Parse(JsonElement element)
	{
		if (element.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		var id = element.TryGetProperty("id", out var idValue) && idValue.ValueKind == JsonValueKind.String
			? idValue.GetString() ?? ""
			: "";
		var type = element.TryGetProperty("type", out var typeValue) && typeValue.ValueKind == JsonValueKind.String
			? typeValue.GetString() ?? ""
			: "";
		var version = element.TryGetProperty("requiredComponentVersion", out var versionValue)
			&& versionValue.ValueKind == JsonValueKind.Number
				? versionValue.GetInt32()
				: 1;
		JsonElement? properties = element.TryGetProperty("properties", out var propertiesValue)
			&& propertiesValue.ValueKind == JsonValueKind.Object
				? propertiesValue
				: null;
		var children = new List<UiNode>();
		if (element.TryGetProperty("children", out var childrenValue) && childrenValue.ValueKind == JsonValueKind.Array)
		{
			foreach (var child in childrenValue.EnumerateArray())
			{
				var parsed = Parse(child);
				if (parsed is not null)
				{
					children.Add(parsed);
				}
			}
		}

		var fallback = element.TryGetProperty("fallback", out var fallbackValue) ? Parse(fallbackValue) : null;
		return new UiNode
		{
			Id = id,
			Type = type,
			RequiredComponentVersion = version,
			Properties = properties,
			Children = children,
			Fallback = fallback
		};
	}

	public JsonElement? Raw(string key)
	{
		if (Properties is { } properties && properties.TryGetProperty(key, out var value))
		{
			return value;
		}

		return null;
	}

	public string? String(string key)
	{
		return Raw(key) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
	}

	public double? Number(string key)
	{
		return Raw(key) is { ValueKind: JsonValueKind.Number } value ? value.GetDouble() : null;
	}

	public bool? Boolean(string key)
	{
		return Raw(key) switch
		{
			{ ValueKind: JsonValueKind.True } => true,
			{ ValueKind: JsonValueKind.False } => false,
			_ => null
		};
	}

	public JsonElement? Record(string key)
	{
		return Raw(key) is { ValueKind: JsonValueKind.Object } value ? value : null;
	}

	public IReadOnlyList<string> Events()
	{
		if (Raw("events") is not { ValueKind: JsonValueKind.Array } value)
		{
			return [];
		}

		var events = new List<string>();
		foreach (var item in value.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.String)
			{
				return [];
			}

			events.Add(item.GetString() ?? "");
		}

		return events;
	}
}
