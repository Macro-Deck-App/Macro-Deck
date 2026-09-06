using System.Text.Json.Serialization;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Domain.Entities;

public class VariableBinding
{
	public required Guid Id { get; set; }

	public required string IntegrationId { get; set; }

	public required string LocalResourceId { get; set; }

	// Persisted rather than re-derived on every load: derivation depends on what else is already
	// registered, so recomputing it would let two bindings swap names when they load in a different
	// order - silently repointing every template that referenced them.
	public required string Name { get; set; }

	public required VariableType Type { get; set; }

	public int? DecimalPlaces { get; set; }

	public DateTime CreatedAt { get; set; }

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? MigratedFrom { get; set; }
}
