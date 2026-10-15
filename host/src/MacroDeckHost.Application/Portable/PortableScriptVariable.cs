using System.Text.Json.Serialization;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Portable;

public sealed class PortableScriptVariable
{
	public Guid ScriptId { get; set; }

	public string Name { get; set; } = string.Empty;

	public VariableType Type { get; set; }

	public string Value { get; set; } = string.Empty;

	public int? DecimalPlaces { get; set; }

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Template { get; set; }
}
