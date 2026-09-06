using System.Text.Json.Serialization;
using MacroDeckHost.Domain.Serialization;

namespace MacroDeckHost.Domain.Entities;

[JsonConverter(typeof(ScriptInputTypeJsonConverter))]
public enum ScriptInputType
{
	Text = 0,
	Numeric = 1,
	Boolean = 2
}

public sealed record ScriptInput
{
	public string Name { get; set; } = string.Empty;

	public ScriptInputType Type { get; set; }

	public string? Label { get; set; }

	public string? Description { get; set; }

	public bool Required { get; set; }

	public string? DefaultValue { get; set; }
}
