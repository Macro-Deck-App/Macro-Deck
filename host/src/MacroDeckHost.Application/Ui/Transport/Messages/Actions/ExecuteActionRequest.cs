using System.Text.Json;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Actions;

public class ExecuteActionRequest
{
	public string IntegrationId { get; set; } = string.Empty;

	public string ActionId { get; set; } = string.Empty;

	public Dictionary<string, JsonElement>? Parameters { get; set; }

	public string? ClientId { get; set; }
}
