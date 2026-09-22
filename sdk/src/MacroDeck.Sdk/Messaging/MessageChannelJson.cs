using System.Text.Json;

namespace MacroDeck.Sdk.Messaging;

internal static class MessageChannelJson
{
	public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
