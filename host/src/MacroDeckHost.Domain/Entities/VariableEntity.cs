using System.Text.Json.Serialization;
using MacroDeckHost.Domain.Enums;
using VariableWriteCapability = MacroDeck.Sdk.Variables.VariableWriteCapability;

namespace MacroDeckHost.Domain.Entities;

public class VariableEntity : BaseEntity
{
	public required string Name { get; set; }

	public required VariableScope Scope { get; set; }

	public string? ScopeRefId { get; set; }

	public required VariableType Type { get; set; }

	public required VariableClassification Classification { get; set; }

	public string? OwnerIntegrationId { get; set; }

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? DefinitionId { get; set; }

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public VariablePresentation? Presentation { get; set; }

	public string Value { get; set; } = string.Empty;

	// The freshness window is a watchdog against a polled provider that stops answering. A pushed
	// variable has no cadence to fall behind, so applying it would expire a value that is simply stable.
	public VariableUpdateMode UpdateMode { get; set; } = VariableUpdateMode.Polled;

	public int? DecimalPlaces { get; set; }

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Unit { get; set; }

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? SemanticKind { get; set; }

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, string>? Attributes { get; set; }

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public double? Min { get; set; }

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public double? Max { get; set; }

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public double? Step { get; set; }

	[JsonIgnore]
	public VariableWriteCapability? Write { get; set; }

	// Computed, never stored: user variables are loaded straight into the registry from
	// user-variables.json and are never re-registered, so a persisted flag would come back false after
	// the first restart and turn every user variable read-only.
	[JsonIgnore]
	public bool CanWrite => Classification == VariableClassification.User || Write is not null;

	[JsonIgnore]
	public bool CommitOnRelease => Write?.CommitOnRelease == true;

	public DateTime UpdatedAt { get; set; }
}
