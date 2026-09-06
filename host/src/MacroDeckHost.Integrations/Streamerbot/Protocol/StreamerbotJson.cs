using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroDeckHost.Integrations.Streamerbot.Protocol;

internal static class StreamerbotJson
{
	public static JsonSerializerOptions Options { get; } = new()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		WriteIndented = false
	};
}
