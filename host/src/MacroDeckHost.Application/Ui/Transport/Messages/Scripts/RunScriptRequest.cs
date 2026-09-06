namespace MacroDeckHost.Application.Ui.Transport.Messages.Scripts;

public class RunScriptRequest
{
	public string Id { get; set; } = string.Empty;

	public string? ClientId { get; set; }

	public int? CallDepth { get; set; }

	public Dictionary<string, object?>? Inputs { get; set; }

	public string? OwnerWidgetId { get; set; }
}
