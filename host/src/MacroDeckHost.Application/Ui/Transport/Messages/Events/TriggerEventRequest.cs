using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Events;

public class TriggerEventRequest
{
	public string EventId { get; set; } = string.Empty;

	public Dictionary<string, JsonElement>? Parameters { get; set; }

	// Host-internal: set only by the MCP server, never read from the wire.
	[JsonIgnore]
	public Guid? McpTokenId { get; set; }
}
