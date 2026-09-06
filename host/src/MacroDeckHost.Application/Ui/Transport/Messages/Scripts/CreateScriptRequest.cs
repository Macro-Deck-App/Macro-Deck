using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Scripts;

public class CreateScriptRequest
{
	public string Name { get; set; } = string.Empty;

	public string? Description { get; set; }

	public string? Flows { get; set; }

	public List<ScriptInput>? Inputs { get; set; }

	public bool RunsOnWidget { get; set; }
}
