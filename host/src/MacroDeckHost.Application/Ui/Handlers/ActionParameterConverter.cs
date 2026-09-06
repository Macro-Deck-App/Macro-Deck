using System.Text.Json;

namespace MacroDeckHost.Application.Ui.Handlers;

internal static class ActionParameterConverter
{
	public static Dictionary<string, object?> ToNullable(IReadOnlyDictionary<string, JsonElement> values)
		=> values.ToDictionary(pair => pair.Key, pair => ToClr(pair.Value));

	private static object? ToClr(JsonElement element)
		=> element.ValueKind switch
		{
			JsonValueKind.String => element.GetString(),
			JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.Null or JsonValueKind.Undefined => null,
			_ => element.GetRawText()
		};
}
