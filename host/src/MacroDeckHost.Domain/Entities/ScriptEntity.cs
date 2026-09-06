namespace MacroDeckHost.Domain.Entities;

public class ScriptEntity : BaseEntity
{
	public required string Name { get; set; }

	public string Description { get; set; } = string.Empty;

	public string Flows { get; set; } = string.Empty;

	public List<ScriptInput> Inputs { get; set; } = [];

	public bool RunsOnWidget { get; set; }

	public DateTime UpdatedAt { get; set; }
}
