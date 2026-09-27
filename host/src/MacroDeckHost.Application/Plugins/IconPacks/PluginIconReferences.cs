using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeckHost.Application.Plugins.IconPacks;

public static class PluginIconReferences
{
	public const string Type = "plugin-icon";

	public const string ResourceOwnerId = "app.macro-deck.plugin-icon";

	public static string SourceId(string pluginId, string key) => $"{pluginId}/{key}";

	public static string SourceIdPrefix(string pluginId) => pluginId + "/";

	public static string? KeyOf(string pluginId, string? sourceId)
	{
		var prefix = SourceIdPrefix(pluginId);
		return sourceId is not null && sourceId.StartsWith(prefix, StringComparison.Ordinal)
			? sourceId[prefix.Length..]
			: null;
	}

	public static bool TryParse(string? reference, out string key, out string name)
	{
		key = string.Empty;
		name = string.Empty;
		var separator = reference?.IndexOf('/') ?? -1;
		if (separator <= 0 || separator == reference!.Length - 1)
		{
			return false;
		}

		key = reference[..separator];
		name = reference[(separator + 1)..];
		return PluginBundledIconPacks.IsValidKey(key) && !name.Contains('/');
	}

	public static string ResourceId(Guid iconId) => $"{ResourceOwnerId}.{iconId:D}";

	public static bool TryParseResourceId(string? resourceId, out Guid iconId)
	{
		iconId = Guid.Empty;
		var prefix = ResourceOwnerId + ".";
		return resourceId is not null &&
			resourceId.StartsWith(prefix, StringComparison.Ordinal) &&
			Guid.TryParseExact(resourceId[prefix.Length..], "D", out iconId);
	}
}
