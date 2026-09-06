using System.Text.Json;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

public class GetWidgetDataSchemasRequest
{
}

public class GetWidgetDataSchemasResponse
{
	public bool Success { get; set; } = true;
	public TransportError? Error { get; set; }

	public Dictionary<string, JsonElement> Schemas { get; set; } = [];
}
