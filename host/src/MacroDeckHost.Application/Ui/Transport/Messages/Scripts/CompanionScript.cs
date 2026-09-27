using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Scripts;

public class CompanionScript
{
	public string Id { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;

	public string Description { get; set; } = string.Empty;

	public List<ScriptInput> Inputs { get; set; } = [];
}
