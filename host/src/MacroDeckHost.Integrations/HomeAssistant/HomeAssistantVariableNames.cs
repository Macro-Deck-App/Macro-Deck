using System.Globalization;
using System.Text;
using MacroDeck.Sdk.Identity;

namespace MacroDeckHost.Integrations.HomeAssistant;

internal sealed record HomeAssistantVariableName(
	string EntityId,
	string StateName,
	string StateDefinitionId,
	string AttributesName,
	string AttributesDefinitionId);

// Compatibility-only, scheduled for removal in a later release. This used to name the two IVariableApi
// variables HomeAssistantWatchedVariables created per watched entity; now it exists solely so
// HomeAssistantWatchedEntityMigration can reproduce those exact names - Build(entityIds) is deterministic,
// so the same entity list always yields the same names - and carry them over onto the new dynamic-variable
// bindings. Nothing else may depend on it: deleting it before every installed profile has migrated would
// orphan any variable template that still references one of these names.
internal static class HomeAssistantVariableNames
{
	public const string NamePrefix = "ha_";

	public const string DefinitionIdPrefix = "entity-";

	private const string AttributesNameSuffix = "_attributes";
	private const string AttributesDefinitionIdSuffix = "-attributes";

	private const int HashTailLength = 9;

	private static readonly int _maxCoreLength = MacroDeckId.MaxDeclaredLocalIdLength -
		DefinitionIdPrefix.Length -
		AttributesDefinitionIdSuffix.Length;

	public static IReadOnlyList<HomeAssistantVariableName> Build(IEnumerable<string> entityIds)
	{
		var used = new HashSet<string>(StringComparer.Ordinal);
		var names = new List<HomeAssistantVariableName>();

		foreach (var entityId in entityIds)
		{
			if (string.IsNullOrWhiteSpace(entityId))
			{
				continue;
			}

			var core = Deduplicate(Core(entityId), used);
			var stateName = NamePrefix + core.Replace('-', '_');

			names.Add(new HomeAssistantVariableName(entityId,
				stateName,
				DefinitionIdPrefix + core,
				stateName + AttributesNameSuffix,
				DefinitionIdPrefix + core + AttributesDefinitionIdSuffix));
		}

		return names;
	}

	private static string Core(string entityId)
	{
		var builder = new StringBuilder(entityId.Length);
		foreach (var character in entityId.ToLowerInvariant())
		{
			if (char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character))
			{
				builder.Append(character);
			}
			else if (builder.Length > 0 && builder[^1] != '-')
			{
				builder.Append('-');
			}
		}

		var core = builder.ToString().Trim('-');
		if (core.Length == 0)
		{
			return "e" + Hash(entityId);
		}

		// A digit cannot start a declared id, and "entity-" already precedes this, but the name side
		// only gets "ha_" - which is a letter, so both stay valid either way.
		return core.Length <= _maxCoreLength
			? core
			: Truncate(core, _maxCoreLength - HashTailLength) + "-" + Hash(entityId);
	}

	private static string Deduplicate(string core, HashSet<string> used)
	{
		if (used.Add(core))
		{
			return core;
		}

		for (var suffix = 2;; suffix++)
		{
			var tail = suffix.ToString(CultureInfo.InvariantCulture);
			var candidate = Truncate(core, _maxCoreLength - tail.Length - 1) + "-" + tail;
			if (used.Add(candidate))
			{
				return candidate;
			}
		}
	}

	private static string Truncate(string core, int length)
	{
		if (core.Length <= length)
		{
			return core;
		}

		var truncated = core[..Math.Max(length, 1)].TrimEnd('-');
		return truncated.Length == 0 ? "e" : truncated;
	}

	private static string Hash(string entityId)
	{
		var hash = 2166136261u;
		foreach (var character in entityId)
		{
			hash = (hash ^ character) * 16777619u;
		}

		return hash.ToString("x8", CultureInfo.InvariantCulture);
	}
}
