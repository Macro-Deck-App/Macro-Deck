using System.Text.Json.Nodes;
using MacroDeckHost.Application.Widgets;

namespace MacroDeckHost.Application.Variables;

public sealed record WidgetVariableReferences(
	IReadOnlySet<string> LabelNames,
	IReadOnlySet<string> StateMappingNames,
	(string IntegrationId, string ActionId)? Provider,
	(string IntegrationId, string ActionId)? IconProvider)
{
	public static readonly WidgetVariableReferences Empty = new(new HashSet<string>(StringComparer.Ordinal),
		new HashSet<string>(StringComparer.Ordinal),
		null,
		null);

	public bool IsEmpty
		=> LabelNames.Count == 0 && StateMappingNames.Count == 0 && Provider is null && IconProvider is null;
}

public static class WidgetVariableReferenceParser
{
	public const string StateMappingKey = "stateMapping";

	// Data that has not been re-saved since the #612 upgrade still carries the old key, and the index
	// has to keep invalidating it - it never gets rewritten just by being indexed.
	private const string LegacyStateBindingKey = "stateBinding";

	private const string ProviderKey = "stateProvider";
	private const string IconProviderKey = "iconProvider";
	private const string VarsPrefix = "vars.";
	private const string TypedReferenceKey = "$var";

	public static WidgetVariableReferences Parse(string? data)
	{
		if (string.IsNullOrEmpty(data))
		{
			return WidgetVariableReferences.Empty;
		}

		var hasLiquid = VariableTemplateRenderer.ContainsLiquid(data);
		var hasStateMapping = data.Contains(StateMappingKey, StringComparison.Ordinal) ||
			data.Contains(LegacyStateBindingKey, StringComparison.Ordinal);
		var hasProvider = data.Contains(ProviderKey, StringComparison.Ordinal);
		var hasIconProvider = data.Contains(IconProviderKey, StringComparison.Ordinal);

		if (!hasLiquid && !hasStateMapping && !hasProvider && !hasIconProvider)
		{
			return WidgetVariableReferences.Empty;
		}

		var liquidNames = CollectNamesAfter(data, VarsPrefix);

		var labelNames = hasLiquid ? liquidNames : WidgetVariableReferences.Empty.LabelNames;

		var mappingNames = WidgetVariableReferences.Empty.StateMappingNames;
		if (hasStateMapping)
		{
			var union = new HashSet<string>(liquidNames, StringComparer.Ordinal);
			union.UnionWith(CollectNamesAfter(data, TypedReferenceKey));
			mappingNames = union;
		}

		var provider = hasProvider ? ReadProviderReference(data, ProviderKey) : null;
		var iconProvider = hasIconProvider ? ReadProviderReference(data, IconProviderKey) : null;

		return labelNames.Count == 0 && mappingNames.Count == 0 && provider is null && iconProvider is null
			? WidgetVariableReferences.Empty
			: new WidgetVariableReferences(labelNames, mappingNames, provider, iconProvider);
	}

	// Dynamic access, vars[name], names nothing after the prefix and is not collected.
	public static IReadOnlySet<string> ReferencedNames(string? template)
		=> VariableTemplateRenderer.ContainsLiquid(template)
			? CollectNamesAfter(template!, VarsPrefix)
			: WidgetVariableReferences.Empty.LabelNames;

	private static (string IntegrationId, string ActionId)? ReadProviderReference(string data, string key)
	{
		var bag = ActionButtonStateJson.ParseDataBag(data);
		if (bag[key] is not JsonObject provider)
		{
			return null;
		}

		var integrationId = provider["integrationId"] is JsonValue integrationValue &&
			integrationValue.TryGetValue<string>(out var integration)
				? integration
				: null;
		var actionId = provider["actionId"] is JsonValue actionValue && actionValue.TryGetValue<string>(out var action)
			? action
			: null;

		return string.IsNullOrEmpty(integrationId) || string.IsNullOrEmpty(actionId)
			? null
			: (integrationId, actionId);
	}

	private static HashSet<string> CollectNamesAfter(string data, string marker)
	{
		var names = new HashSet<string>(StringComparer.Ordinal);
		var searchFrom = 0;

		while (true)
		{
			var markerAt = data.IndexOf(marker, searchFrom, StringComparison.Ordinal);
			if (markerAt < 0)
			{
				return names;
			}

			searchFrom = markerAt + marker.Length;
			var nameAt = SkipSeparators(data, searchFrom);
			if (nameAt < data.Length && ReadIdentifier(data, nameAt) is { Length: > 0 } name)
			{
				names.Add(name);
			}
		}
	}

	private static int SkipSeparators(string data, int index)
	{
		while (index < data.Length && data[index] is '"' or '\\' or ':' or ' ' or '\t' or '\r' or '\n')
		{
			index++;
		}

		return index;
	}

	private static string ReadIdentifier(string data, int start)
	{
		var end = start;
		while (end < data.Length && IsIdentifierChar(data[end]))
		{
			end++;
		}

		return end == start ? string.Empty : data[start..end];
	}

	private static bool IsIdentifierChar(char c)
		=> c is >= 'a' and <= 'z' || c is >= 'A' and <= 'Z' || c is >= '0' and <= '9' || c == '_';
}
