using System.Text.Json.Serialization;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Domain.Entities;

public sealed record SharedVariable
{
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public Guid? UserVariableId { get; init; }

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? OwnerIntegrationId { get; init; }

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? DefinitionId { get; init; }

	public required string Name { get; init; }

	public required VariableType Type { get; init; }
}
