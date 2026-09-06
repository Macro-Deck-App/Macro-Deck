using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroDeckHost.Integrations.Meld.Protocol;

internal static class QWebChannelJson
{
	public static JsonSerializerOptions Options { get; } = new()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		WriteIndented = false
	};
}
