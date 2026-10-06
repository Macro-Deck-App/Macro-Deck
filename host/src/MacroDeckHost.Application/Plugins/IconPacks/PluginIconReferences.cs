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

	public const string AppearanceSuffix = ".a";

	public static string ResourceId(Guid iconId, bool hasAppearances = false)
		=> hasAppearances ? $"{ResourceOwnerId}.{iconId:D}{AppearanceSuffix}" : $"{ResourceOwnerId}.{iconId:D}";

	public static bool TryParseResourceId(string? resourceId, out Guid iconId)
		=> TryParseResourceId(resourceId, out iconId, out _);

	public static bool TryParseResourceId(string? resourceId, out Guid iconId, out bool appearanceAware)
	{
		iconId = Guid.Empty;
		appearanceAware = false;
		var prefix = ResourceOwnerId + ".";
		if (resourceId is null || !resourceId.StartsWith(prefix, StringComparison.Ordinal))
		{
			return false;
		}

		var id = resourceId[prefix.Length..];
		if (id.EndsWith(AppearanceSuffix, StringComparison.Ordinal))
		{
			appearanceAware = true;
			id = id[..^AppearanceSuffix.Length];
		}

		return Guid.TryParseExact(id, "D", out iconId);
	}
}
