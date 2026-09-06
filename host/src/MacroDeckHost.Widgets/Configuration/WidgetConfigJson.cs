using System.Text.Json;

namespace MacroDeckHost.Widgets.Configuration;

/// <summary>
/// Reads the handful of raw JSON shapes every widget's configuration tree seeds itself from, but that no
/// widget's own <c>*WidgetData.Parse</c> models: <c>border</c> and <c>flows</c> are painted and run by the
/// tile around a widget's view rather than by the view itself (see <c>MusicPlayerWidgetData</c>), so they
/// have nowhere else to be read from here.
/// </summary>
internal static class WidgetConfigJson
{
	private static readonly JsonElement _emptyObject = JsonDocument.Parse("{}").RootElement;
	private static readonly JsonElement _emptyArray = JsonDocument.Parse("[]").RootElement;

	public static string? ReadString(JsonElement data, string name)
		=> data.ValueKind == JsonValueKind.Object &&
			data.TryGetProperty(name, out var value) &&
			value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;

	public static bool? ReadBool(JsonElement data, string name)
		=> data.ValueKind == JsonValueKind.Object &&
			data.TryGetProperty(name, out var value) &&
			value.ValueKind is JsonValueKind.True or JsonValueKind.False
				? value.GetBoolean()
				: null;

	public static double? ReadDouble(JsonElement data, string name)
		=> data.ValueKind == JsonValueKind.Object &&
			data.TryGetProperty(name, out var value) &&
			value.ValueKind == JsonValueKind.Number &&
			value.TryGetDouble(out var number) &&
			double.IsFinite(number)
				? number
				: null;

	/// <summary>The nested <c>border</c> object, or an empty one when absent or malformed - so
	/// <c>border.style</c>/<c>border.color</c> read as absent rather than throwing.</summary>
	public static JsonElement ReadObject(JsonElement data, string name)
		=> data.ValueKind == JsonValueKind.Object &&
			data.TryGetProperty(name, out var value) &&
			value.ValueKind == JsonValueKind.Object
				? value
				: _emptyObject;

	/// <summary>
	/// The stored <c>flows</c> key as the JSON array the actions-list editor renders: an already-array value
	/// is used verbatim, a nested JSON string is parsed, and anything absent or unreadable seeds an empty
	/// list rather than failing the session. Never written back by this tree unless the user actually edits
	/// the flows - see ADR 0050.
	/// </summary>
	public static JsonElement ReadFlows(JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("flows", out var flows))
		{
			return _emptyArray;
		}

		if (flows.ValueKind == JsonValueKind.Array)
		{
			return flows;
		}

		if (flows.ValueKind != JsonValueKind.String)
		{
			return _emptyArray;
		}

		var raw = flows.GetString();

		if (string.IsNullOrWhiteSpace(raw))
		{
			return _emptyArray;
		}

		try
		{
			using var document = JsonDocument.Parse(raw);

			return document.RootElement.ValueKind == JsonValueKind.Array
				? document.RootElement.Clone()
				: _emptyArray;
		}
		catch (JsonException)
		{
			return _emptyArray;
		}
	}
}
