using System.Text.Json.Serialization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Scripts;

public class RunScriptRequest
{
	public string Id { get; set; } = string.Empty;

	public string? ClientId { get; set; }

	public int? CallDepth { get; set; }

	public Dictionary<string, object?>? Inputs { get; set; }

	public string? OwnerWidgetId { get; set; }

	// Host-internal: set only by the MCP server, never read from the wire.
	[JsonIgnore]
	public Guid? McpTokenId { get; set; }
}
