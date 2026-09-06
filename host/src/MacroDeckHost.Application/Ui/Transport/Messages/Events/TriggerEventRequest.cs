using System.Text.Json;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Events;

public class TriggerEventRequest
{
	public string EventId { get; set; } = string.Empty;

	public Dictionary<string, JsonElement>? Parameters { get; set; }
}
