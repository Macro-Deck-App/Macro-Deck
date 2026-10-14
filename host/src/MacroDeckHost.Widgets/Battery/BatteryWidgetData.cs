using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Sdk.Batteries;
using MacroDeck.Ui.Config;

namespace MacroDeckHost.Widgets.Battery;

public sealed record BatteryWidgetData
{
	public const int MaxDevices = 8;

	public const string ModeOne = "one";
	public const string ModeAll = "all";
	public const string ModeSelection = "selection";

	public const string StyleRing = "ring";
	public const string StyleBar = "bar";

	public const string ColorByLevel = "level";
	public const string ColorSingle = "single";

	public const string ModeKey = "mode";
	public const string DeviceKey = "device";
	public const string SelectionKey = "selection";
	public const string SelectionDevicesKey = "devices";
	public const string StyleKey = "style";
	public const string ShowPercentageKey = "showPercentage";
	public const string ShowNameKey = "showName";
	public const string ShowChargingBadgeKey = "showChargingBadge";
	public const string ColorModeKey = "colorMode";
	public const string ChargingColorKey = "chargingColor";
	public const string ColorKey = "color";

	public string Mode { get; init; } = ModeAll;

	public BatteryDeviceReference? Device { get; init; }

	public IReadOnlyList<BatteryDeviceReference> Selection { get; init; } = [];

	public string Style { get; init; } = StyleRing;

	public bool ShowPercentage { get; init; } = true;

	public bool ShowName { get; init; } = true;

	public bool ShowChargingBadge { get; init; } = true;

	public string ColorMode { get; init; } = ColorByLevel;

	public bool ChargingColor { get; init; } = true;

	public UiThresholds? Thresholds { get; init; }

	public string? Color { get; init; }

	public string? BackgroundColor { get; init; }

	public bool IsBar => Style == StyleBar;

	public bool IsSingleColor => ColorMode == ColorSingle;

	public static BatteryWidgetData Parse(JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object)
		{
			return new BatteryWidgetData();
		}

		var mode = ReadString(data, ModeKey);

		return new BatteryWidgetData
		{
			Mode = mode is ModeOne or ModeSelection ? mode : ModeAll,
			Device = data.TryGetProperty(DeviceKey, out var device) ? BatteryDeviceReference.Read(device) : null,
			Selection = ReadSelection(data),
			Style = ReadString(data, StyleKey) == StyleBar ? StyleBar : StyleRing,
			ShowPercentage = ReadBool(data, ShowPercentageKey) ?? true,
			ShowName = ReadBool(data, ShowNameKey) ?? true,
			ShowChargingBadge = ReadBool(data, ShowChargingBadgeKey) ?? true,
			ColorMode = ReadString(data, ColorModeKey) == ColorSingle ? ColorSingle : ColorByLevel,
			ChargingColor = ReadBool(data, ChargingColorKey) ?? true,
			Thresholds = WidgetThresholds.ReadStored(data),
			Color = WidgetColor.Normalize(ReadString(data, ColorKey)),
			BackgroundColor = WidgetColor.NormalizeBackground(ReadString(data, "backgroundColor"))
		};
	}

	internal static IReadOnlyList<BatteryDeviceReference> ReadSelection(JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object
			|| !data.TryGetProperty(SelectionKey, out var selection)
			|| selection.ValueKind != JsonValueKind.Object
			|| !selection.TryGetProperty(SelectionDevicesKey, out var devices)
			|| devices.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		var seen = new HashSet<string>(StringComparer.Ordinal);
		var result = new List<BatteryDeviceReference>();

		foreach (var item in devices.EnumerateArray())
		{
			if (BatteryDeviceReference.Read(item) is { } reference && seen.Add(reference.Id))
			{
				result.Add(reference);
			}
		}

		return result;
	}

	private static string? ReadString(JsonElement data, string name)
	{
		return data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
	}

	private static bool? ReadBool(JsonElement data, string name)
	{
		return data.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
			? value.GetBoolean()
			: null;
	}
}

public sealed record BatteryDeviceReference(string Id, string? Name, string Kind)
{
	public static BatteryDeviceReference? Read(JsonElement element)
	{
		if (element.ValueKind != JsonValueKind.Object
			|| !element.TryGetProperty("id", out var id)
			|| id.ValueKind != JsonValueKind.String
			|| id.GetString() is not { Length: > 0 } value)
		{
			return null;
		}

		var name = element.TryGetProperty("name", out var stored) && stored.ValueKind == JsonValueKind.String
			? stored.GetString()
			: null;
		var kind = element.TryGetProperty("kind", out var storedKind) && storedKind.ValueKind == JsonValueKind.String
			? storedKind.GetString()
			: null;

		return new BatteryDeviceReference(value,
			name is { Length: > 0 } ? name : null,
			BatteryDeviceKinds.Normalize(kind));
	}

	public static BatteryDeviceReference Of(BatteryDeviceSnapshot device)
	{
		ArgumentNullException.ThrowIfNull(device);

		return new BatteryDeviceReference(device.Id, device.Name, BatteryDeviceKinds.Normalize(device.Kind));
	}

	public JsonObject ToJson()
	{
		var json = new JsonObject { ["id"] = Id };

		if (Name is { Length: > 0 } name)
		{
			json["name"] = name;
		}

		json["kind"] = Kind;

		return json;
	}
}
