using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroDeckHost.Integrations.HomeAssistant.Protocol;

internal static class HomeAssistantJson
{
	public static JsonSerializerOptions Options { get; } = new()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		WriteIndented = false
	};
}
