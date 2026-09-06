using MacroDeck.Sdk.Variables;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Variables;

/// <summary>
/// Reads everything a provider declared alongside a variable - presentation, static attributes, write
/// capability. Every registration path goes through here so a provider that fills these in cannot have
/// them silently dropped by one path but not another.
/// </summary>
public static class VariableDeclarationFactory
{
	// The typed attribute names the template namespace already spells; an open-map entry under one of
	// them is dropped rather than allowed to shadow the value the host computed.
	private static readonly HashSet<string> _reservedAttributeKeys = new(StringComparer.Ordinal)
	{
		"unit",
		"semantic_kind",
		"decimal_places",
		"min",
		"max",
		"step"
	};

	public static VariableDeclaration From(VariableDefinition definition)
	{
		var configuration = Usable(definition.Configuration);
		var presentation = definition.DisplayName.IsEmpty && configuration is null
			? null
			: new VariablePresentation(definition.DisplayName, configuration?.Key, configuration?.Name ?? default);

		return new VariableDeclaration
		{
			Presentation = presentation,
			DecimalPlaces = definition.DecimalPlaces,
			Unit = Trimmed(definition.Unit),
			SemanticKind = Trimmed(definition.SemanticKind),
			Attributes = Clamp(definition.Attributes),
			Write = definition.Write
		};
	}

	/// <summary>
	/// A blank key cannot identify a group, so it is treated as no configuration rather than failing the
	/// registration - a bad group label must never cost the user the variable itself.
	/// </summary>
	private static VariableConfiguration? Usable(VariableConfiguration? configuration)
		=> configuration is not null && !string.IsNullOrWhiteSpace(configuration.Key) ? configuration : null;

	private static string? Trimmed(string? value)
		=> string.IsNullOrWhiteSpace(value) ? null : value.Trim();

	// An offending entry is dropped, never a registration failure: an unbounded or malformed open map
	// must not cost the user a working variable. The size bound is not cosmetic - every attribute rides
	// the variable broadcast, and a 200-variable chunk carrying unbounded maps overruns the UI socket's
	// message ceiling, which aborts the socket rather than truncating.
	private static Dictionary<string, string>? Clamp(IReadOnlyDictionary<string, string>? attributes)
	{
		if (attributes is null || attributes.Count == 0)
		{
			return null;
		}

		var clamped = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var entry in attributes)
		{
			if (clamped.Count == VariableLimits.MaxAttributeEntries)
			{
				break;
			}

			if (entry.Value is null ||
				entry.Value.Length > VariableLimits.MaxAttributeValueLength ||
				_reservedAttributeKeys.Contains(entry.Key) ||
				!IsAttributeKey(entry.Key))
			{
				continue;
			}

			clamped[entry.Key] = entry.Value;
		}

		return clamped.Count == 0 ? null : clamped;
	}

	private static bool IsAttributeKey(string key)
	{
		if (key.Length == 0)
		{
			return false;
		}

		foreach (var character in key)
		{
			if (character is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '_'))
			{
				return false;
			}
		}

		return true;
	}
}
