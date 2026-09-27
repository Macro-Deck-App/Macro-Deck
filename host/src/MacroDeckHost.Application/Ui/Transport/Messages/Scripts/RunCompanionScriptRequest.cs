namespace MacroDeckHost.Application.Ui.Transport.Messages.Scripts;

public class RunCompanionScriptRequest
{
	public Dictionary<string, object?>? Inputs { get; set; }

	public string? ClientId { get; set; }
}
