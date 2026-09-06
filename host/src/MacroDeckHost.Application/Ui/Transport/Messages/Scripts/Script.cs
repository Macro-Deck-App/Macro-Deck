using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Scripts;

public class Script
{
	public string Id { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;

	public string Description { get; set; } = string.Empty;

	public string Flows { get; set; } = string.Empty;

	public List<ScriptInput> Inputs { get; set; } = [];

	public bool RunsOnWidget { get; set; }

	public DateTime CreatedAt { get; set; }

	public DateTime UpdatedAt { get; set; }
}
