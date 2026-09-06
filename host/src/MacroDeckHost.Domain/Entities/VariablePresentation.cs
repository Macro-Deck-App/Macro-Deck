using MacroDeck.Localization;

namespace MacroDeckHost.Domain.Entities;

/// <summary>
/// How a provider wants one of its variables presented: a readable name instead of the canonical one, and
/// the configured instance it belongs to. Absent for user variables and for providers that declare neither.
/// </summary>
/// <remarks>
/// Grouped into one nullable reference rather than sitting flat on <see cref="VariableEntity"/> because a
/// <see cref="LocalizedText"/> is a non-nullable struct, which cannot be omitted from the user-variable JSON
/// store with <c>JsonIgnoreCondition.WhenWritingNull</c>.
/// </remarks>
public sealed record VariablePresentation(
	LocalizedText DisplayName,
	string? ConfigurationKey,
	LocalizedText ConfigurationName);
