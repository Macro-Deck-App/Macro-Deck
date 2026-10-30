using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Actions;

public class ExecuteActionRequest
{
	public string IntegrationId { get; set; } = string.Empty;

	public string ActionId { get; set; } = string.Empty;

	public Dictionary<string, JsonElement>? Parameters { get; set; }

	public string? ClientId { get; set; }

	// Host-internal: set only by the MCP server, never read from the wire.
	[JsonIgnore]
	public Guid? McpTokenId { get; set; }

	[JsonIgnore]
	public IReadOnlyCollection<string>? McpConsentedVariables { get; set; }
}
