using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Portable;

public sealed class PortableScript
{
	public Guid Id { get; set; }

	public string Name { get; set; } = string.Empty;

	public string Description { get; set; } = string.Empty;

	public string Flows { get; set; } = string.Empty;

	public List<ScriptInput> Inputs { get; set; } = [];

	public bool RunsOnWidget { get; set; }
}
