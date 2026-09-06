using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Scripts;

public class UpdateScriptRequest
{
	public string Id { get; set; } = string.Empty;

	public string? Name { get; set; }

	public string? Description { get; set; }

	public string? Flows { get; set; }

	/// <summary>Null leaves the stored declarations untouched.</summary>
	public List<ScriptInput>? Inputs { get; set; }

	/// <summary>Null leaves the stored flag untouched.</summary>
	public bool? RunsOnWidget { get; set; }
}
